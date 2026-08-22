using System;
using System.Collections;
using TableFootball.Net;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace TableFootball.UI
{
    /// <summary>
    /// The online front door: host a table and read out the code, join one with a code, or take
    /// whatever open table the service can find.
    ///
    /// Like <see cref="MainMenu"/>, this reports through callbacks and never starts a match itself —
    /// <see cref="GameFlow"/> owns the sequence. It knows about <see cref="OnlineSession"/> and
    /// nothing else: no rods, no NetworkManager, no match state.
    ///
    /// Session callbacks are marshalled through <see cref="Update"/> rather than touching the UI
    /// where they arrive. The SDK does raise them on the main thread, but a player who backs out
    /// during a call would otherwise have their panel rebuilt after it had been torn down.
    /// </summary>
    public class OnlineMenu : MonoBehaviour
    {
        /// <summary>Relay join codes are six characters.</summary>
        private const int CodeLength = 6;

        private enum Screen
        {
            Choose,
            Working,
            Waiting,
            /// <summary>Quick match, which has nothing to show but the fact that it is still going.</summary>
            Searching
        }

        private GameObject root;
        private CanvasGroup group;

        private GameObject chooseGroup;
        private GameObject waitingGroup;
        private GameObject searchingGroup;
        private GameObject panelRoot;
        private TMP_InputField codeField;
        private TextMeshProUGUI statusText;
        private TextMeshProUGUI codeDisplay;
        private MenuButton hostButton;
        private MenuButton joinButton;
        private MenuButton quickButton;

        private bool opponentArrived;

        /// <summary>How long two players may sit in one session without the match ever starting.</summary>
        private const float ConnectTimeoutSeconds = 25f;

        private Coroutine connectTimeout;

        /// <summary>Raised once both players are in and the table can start.</summary>
        public Action OnMatchReady;

        /// <summary>Raised when the player backs out, after the session has been left.</summary>
        public Action OnBack;

        public bool IsOpen => root != null && root.activeSelf;

        public void Build(Transform canvasRoot)
        {
            root = UIFactory.Child(canvasRoot, "OnlineMenu");
            UIFactory.Stretch(UIFactory.Rt(root));
            group = root.AddComponent<CanvasGroup>();

            UIFactory.Backdrop(root.transform);

            var title = UIFactory.Text(root.transform, "ONLINE", ArcadeTheme.FsTitle, ArcadeTheme.Ink,
                                       display: true, bold: true, upper: true, tracking: 8f);
            var trt = UIFactory.Rt(title.gameObject);
            trt.anchorMin = new Vector2(0f, 1f);
            trt.anchorMax = new Vector2(1f, 1f);
            trt.pivot = new Vector2(0.5f, 1f);
            trt.offsetMin = new Vector2(0f, -170f);
            trt.offsetMax = new Vector2(0f, -70f);

            var panel = UIFactory.Panel(root.transform, "OnlinePanel");
            panelRoot = panel;
            var prt = UIFactory.Rt(panel);
            prt.anchorMin = new Vector2(0.5f, 0.5f);
            prt.anchorMax = new Vector2(0.5f, 0.5f);
            prt.pivot = new Vector2(0.5f, 0.5f);
            prt.sizeDelta = new Vector2(560f, 460f);
            prt.anchoredPosition = new Vector2(0f, -10f);

            BuildChoose(panel.transform);
            BuildWaiting(panel.transform);
            BuildSearching(root.transform);
            BuildStatus(root.transform);
            BuildBack(root.transform);

            Show(Screen.Choose);
            root.SetActive(false);
        }

        private static GameObject BuildColumn(Transform parent, string name)
        {
            var column = UIFactory.Child(parent, name);
            UIFactory.Stretch(UIFactory.Rt(column), 36f, 36f, 36f, 36f);

            var layout = column.AddComponent<VerticalLayoutGroup>();
            layout.spacing = ArcadeTheme.Lg;
            layout.childAlignment = TextAnchor.MiddleCenter;
            layout.childForceExpandWidth = true;
            layout.childForceExpandHeight = false;
            layout.childControlWidth = true;
            layout.childControlHeight = true;

            return column;
        }

        private void BuildChoose(Transform parent)
        {
            chooseGroup = BuildColumn(parent, "ChooseGroup");

            hostButton = UIFactory.Button(chooseGroup.transform, "Host Table",
                                          MenuButton.Variant.Primary, Host);

            quickButton = UIFactory.Button(chooseGroup.transform, "Quick Match",
                                           MenuButton.Variant.Neutral, QuickMatch);

            // A rule with the label sitting in it, rather than a line of muted text drifting between
            // two buttons. Everything below it is one thing — join a table someone else opened — and
            // the divider is what says so.
            BuildDivider(chooseGroup.transform, "or join with a code");

            codeField = UIFactory.CodeInput(chooseGroup.transform, "code", CodeLength);
            joinButton = UIFactory.Button(chooseGroup.transform, "Join", MenuButton.Variant.Neutral, Join);
        }

        /// <summary>A horizontal rule broken by a caption, separating the two ways in.</summary>
        private static void BuildDivider(Transform parent, string caption)
        {
            var row = UIFactory.Child(parent, "Divider");
            row.AddComponent<LayoutElement>().preferredHeight = 28f;

            var h = row.AddComponent<HorizontalLayoutGroup>();
            h.childAlignment = TextAnchor.MiddleCenter;
            h.spacing = ArcadeTheme.Md;
            h.childForceExpandWidth = false; h.childForceExpandHeight = false;
            h.childControlWidth = true; h.childControlHeight = true;

            Rule(row.transform);

            var t = UIFactory.Text(row.transform, caption, ArcadeTheme.FsCaption, ArcadeTheme.InkMuted,
                                   display: false, bold: true, upper: true, tracking: 4f);
            var tle = t.gameObject.AddComponent<LayoutElement>();
            tle.preferredWidth = 210f;
            tle.preferredHeight = 20f;

            Rule(row.transform);
        }

        private static void Rule(Transform parent)
        {
            var go = UIFactory.Child(parent, "Rule");
            UIFactory.RoundedImage(go, ArcadeTheme.RadSm, ArcadeTheme.Line, false);
            var le = go.AddComponent<LayoutElement>();
            le.preferredWidth = 120f;
            le.flexibleWidth = 1f;
            le.preferredHeight = 2f;
        }

        private void BuildWaiting(Transform parent)
        {
            waitingGroup = BuildColumn(parent, "WaitingGroup");

            UIFactory.Text(waitingGroup.transform, "your code", ArcadeTheme.FsCaption,
                           ArcadeTheme.InkMuted, display: false, bold: true, upper: true, tracking: 4f);

            // The one thing on this screen the player has to read to another human, so it gets the
            // score font's weight and the gold that every other "this is the thing" uses.
            codeDisplay = UIFactory.Text(waitingGroup.transform, "------", ArcadeTheme.FsScore,
                                         ArcadeTheme.Gold, display: true, bold: true, upper: true,
                                         tracking: 14f);
            var le = codeDisplay.gameObject.AddComponent<LayoutElement>();
            le.preferredHeight = 110f;
            le.minHeight = 110f;

            UIFactory.Text(waitingGroup.transform, "waiting for an opponent", ArcadeTheme.FsBody,
                           ArcadeTheme.Ink, display: false, bold: true, upper: true, tracking: 4f);
        }

        /// <summary>
        /// Quick match's wait: a ring and the word, on the backdrop with no panel behind it.
        ///
        /// Sits outside the panel rather than inside it because there is nothing to frame. Quick
        /// match used to land on the hosting screen and show a join code, which is a code for a
        /// table nobody was ever going to be told about — the player has no one to read it to. All
        /// that is true here is that the search is still running, so that is all it says.
        /// </summary>
        private void BuildSearching(Transform parent)
        {
            searchingGroup = UIFactory.Child(parent, "SearchingGroup");
            var rt = UIFactory.Rt(searchingGroup);
            rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 0.5f);
            rt.pivot = new Vector2(0.5f, 0.5f);
            rt.sizeDelta = new Vector2(420f, 200f);
            rt.anchoredPosition = new Vector2(0f, -10f);

            UIFactory.Loader(searchingGroup.transform);
            UIFactory.Stretch(UIFactory.Rt(searchingGroup.transform.GetChild(0).gameObject), 0);
        }

        private void BuildStatus(Transform parent)
        {
            // Mostly wording from this file, but ShowInviteDeclined puts a friend's chosen name into
            // it, and one line is either safe to hand a name or it is not.
            statusText = UIFactory.Text(parent, string.Empty, ArcadeTheme.FsBody, ArcadeTheme.Red,
                                        display: false, bold: true, upper: true, tracking: 3f,
                                        richText: false);
            var rt = UIFactory.Rt(statusText.gameObject);
            rt.anchorMin = new Vector2(0.5f, 0f);
            rt.anchorMax = new Vector2(0.5f, 0f);
            rt.pivot = new Vector2(0.5f, 0f);
            rt.sizeDelta = new Vector2(700f, 40f);
            rt.anchoredPosition = new Vector2(0f, 190f);
        }

        private void BuildBack(Transform parent)
        {
            var holder = UIFactory.Child(parent, "BackHolder");
            var rt = UIFactory.Rt(holder);
            rt.anchorMin = new Vector2(0.5f, 0f);
            rt.anchorMax = new Vector2(0.5f, 0f);
            rt.pivot = new Vector2(0.5f, 0f);
            rt.sizeDelta = new Vector2(260f, 62f);
            rt.anchoredPosition = new Vector2(0f, 110f);

            var layout = holder.AddComponent<VerticalLayoutGroup>();
            layout.childForceExpandWidth = true;
            layout.childForceExpandHeight = false;
            layout.childControlWidth = true;
            layout.childControlHeight = true;

            UIFactory.Button(holder.transform, "Back", MenuButton.Variant.Ghost, Back);
        }

        // ---------- state ----------

        public void Open()
        {
            if (root == null)
            {
                return;
            }

            opponentArrived = false;
            CancelConnectTimeout();
            codeField.text = string.Empty;
            SetStatus(string.Empty);
            Show(Screen.Choose);

            // Removed first so reopening cannot stack a second handler onto the static event, which
            // outlives this panel and would then fire the match ready twice.
            OnlineSession.OnPlayerCountChanged -= HandlePlayerCount;
            OnlineSession.OnPlayerCountChanged += HandlePlayerCount;

            root.SetActive(true);
            root.transform.SetAsLastSibling();
            group.blocksRaycasts = true;
        }

        public void Close()
        {
            OnlineSession.OnPlayerCountChanged -= HandlePlayerCount;

            // The match starting is what closes this screen, so reaching here means the wait ended
            // one way or another and the timeout has nothing left to fire about.
            CancelConnectTimeout();

            if (root != null)
            {
                group.blocksRaycasts = false;
                root.SetActive(false);
            }
        }

        private void OnDestroy()
        {
            OnlineSession.OnPlayerCountChanged -= HandlePlayerCount;
        }

        private void Show(Screen next)
        {
            bool choosing = next == Screen.Choose;
            bool searching = next == Screen.Searching;

            if (chooseGroup != null) chooseGroup.SetActive(choosing);
            if (waitingGroup != null) waitingGroup.SetActive(next == Screen.Waiting);
            if (searchingGroup != null) searchingGroup.SetActive(searching);

            // The panel steps aside for the loader entirely — an empty framed box behind a spinner
            // is the "black box" this screen used to be.
            if (panelRoot != null) panelRoot.SetActive(!searching);

            // Working is the Choose screen with everything switched off, rather than a fourth panel:
            // the player can still see what they asked for while it happens.
            bool usable = choosing;
            if (hostButton != null) hostButton.interactable = usable;
            if (joinButton != null) joinButton.interactable = usable;
            if (quickButton != null) quickButton.interactable = usable;
            if (codeField != null) codeField.interactable = usable;
        }

        /// <summary>
        /// The opponent has been recorded by the session, but the two machines have not finished
        /// connecting to each other.
        ///
        /// This screen exists because those are separate moments and the gap between them is seconds,
        /// not milliseconds. The menu used to hand straight over to the match here, which is what let
        /// one player start playing while the other was still connecting.
        /// </summary>
        public void ShowConnecting()
        {
            Show(Screen.Working);
            SetStatus("OPPONENT FOUND — CONNECTING…");

            if (connectTimeout != null) StopCoroutine(connectTimeout);
            connectTimeout = StartCoroutine(GiveUpConnecting());
        }

        /// <summary>
        /// The end of the wait, for when the two machines never manage to reach each other.
        ///
        /// The lobby saying there are two players and the two players being able to see each other
        /// are separate facts, and the second one can simply never arrive — a Relay allocation that
        /// does not complete, or two clients that turn out to be the same signed-in player, which the
        /// lobby is happy to seat twice and the transport is not. There was nothing to do about it
        /// from here but wait forever, which is what the screen did.
        ///
        /// Generous on purpose: a slow phone on a slow network genuinely takes ten seconds or more,
        /// and cutting a connection short that would have worked is the worse failure of the two.
        /// </summary>
        private IEnumerator GiveUpConnecting()
        {
            yield return new WaitForSecondsRealtime(ConnectTimeoutSeconds);

            connectTimeout = null;

            Debug.LogError($"Online: {ConnectTimeoutSeconds:0}s after both players joined the " +
                           "session, the match still had not started. The two machines never " +
                           "completed their handshake — check the log above for what the guest " +
                           "reported, and that the two players are not the same signed-in account.");

            // Left, not merely abandoned: the table would otherwise sit on the service and refuse
            // this player their next match for still being in one.
            _ = OnlineSession.LeaveAsync();

            SetStatus("COULDN'T REACH YOUR OPPONENT");
            Show(Screen.Choose);
        }

        private void CancelConnectTimeout()
        {
            if (connectTimeout != null)
            {
                StopCoroutine(connectTimeout);
                connectTimeout = null;
            }
        }

        /// <summary>
        /// Shows the waiting screen for a table this menu did not open.
        ///
        /// Inviting a friend hosts a table from the friends screen, and the player then has to end up
        /// somewhere that shows the code and waits — which is this screen, already built for exactly
        /// that. Without this they would arrive at the Choose screen and be offered a Host button for
        /// a table they are already hosting.
        /// </summary>
        public void ShowHosting()
        {
            codeDisplay.text = OnlineSession.JoinCode;
            SetStatus("INVITE SENT");
            Show(Screen.Waiting);
        }

        /// <summary>
        /// The friend this player invited has said no.
        ///
        /// Shown on the waiting screen they are already reading, in the red the other failures use.
        /// The flow takes them out from here — there is nothing left on this screen for them, and a
        /// table opened for one specific person is not one to keep sitting at once that person has
        /// answered.
        /// </summary>
        public void ShowInviteDeclined(string who)
        {
            string name = string.IsNullOrWhiteSpace(who) ? "YOUR FRIEND" : who.ToUpperInvariant();
            SetStatus($"{name} CAN'T PLAY RIGHT NOW");
        }

        /// <summary>
        /// The table was gone by the time this player answered an invitation, or the code was stale.
        /// Says so, and leaves them on the screen that offers the other ways in rather than bouncing
        /// them all the way back to the main menu.
        /// </summary>
        public void ShowJoinFailed(string message)
        {
            SetStatus(string.IsNullOrWhiteSpace(message)
                ? "COULD NOT JOIN THAT TABLE"
                : message.ToUpperInvariant());

            Show(Screen.Choose);
        }

        private void SetStatus(string message)
        {
            if (statusText != null)
            {
                statusText.text = message ?? string.Empty;
            }
        }

        /// <summary>
        /// The session raises this from its own callback; the panel is only touched from
        /// <see cref="Update"/>. See the class summary.
        /// </summary>
        private void HandlePlayerCount(int players)
        {
            if (players >= 2)
            {
                opponentArrived = true;
            }
        }

        private void Update()
        {
            if (opponentArrived && IsOpen)
            {
                opponentArrived = false;
                OnMatchReady?.Invoke();
            }
        }

        // ---------- actions ----------

        private async void Host()
        {
            Show(Screen.Working);
            SetStatus("CREATING TABLE…");

            if (await OnlineSession.HostAsync())
            {
                codeDisplay.text = OnlineSession.JoinCode;
                SetStatus(string.Empty);
                Show(Screen.Waiting);
            }
            else
            {
                SetStatus(OnlineSession.LastError);
                Show(Screen.Choose);
            }
        }

        private async void Join()
        {
            Show(Screen.Working);
            SetStatus("JOINING…");

            // Success is not handled here: joining a table that already has its host puts the player
            // count at two immediately, so OnMatchReady fires through the normal path.
            if (!await OnlineSession.JoinAsync(codeField.text))
            {
                SetStatus(OnlineSession.LastError);
                Show(Screen.Choose);
            }
        }

        private async void QuickMatch()
        {
            // The loader carries the message, so no status line as well — two things saying "still
            // working" is one more than the player needs.
            SetStatus(string.Empty);
            Show(Screen.Searching);

            if (!await OnlineSession.QuickMatchAsync())
            {
                SetStatus(OnlineSession.LastError);
                Show(Screen.Choose);
                return;
            }

            // Matchmaking either found a table, which starts the match through the player count, or
            // opened one and is now waiting for somebody to walk into it. Either way the loader stays
            // up: hosting a public table is not something the player did on purpose and there is
            // nobody to hand a code to, so it is still just a wait.
            if (OnlineSession.IsHost)
            {
                SetStatus(string.Empty);
            }
        }

        private async void Back()
        {
            SetStatus(string.Empty);

            // Left before closing: an abandoned session would otherwise sit on the service holding a
            // table open, and this player would be refused their next one for already being in it.
            await OnlineSession.LeaveAsync();

            Close();
            OnBack?.Invoke();
        }
    }
}
