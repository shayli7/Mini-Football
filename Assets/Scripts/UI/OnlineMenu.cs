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
        private MenuButton rankedButton;
        private MenuButton hostButton;
        private MenuButton joinButton;
        private MenuButton quickButton;

        private bool opponentArrived;

        /// <summary>How long two players may sit in one session without the match ever starting.</summary>
        private const float ConnectTimeoutSeconds = 25f;

        private Coroutine connectTimeout;

        /// <summary>Raised once both players are in and the table can start.</summary>
        public Action OnMatchReady;

        /// <summary>Raised when the player chooses Ranked — opens the league screen / ranked queue.</summary>
        public Action OnOpenRanked;

        /// <summary>Raised when the player backs out, after the session has been left.</summary>
        public Action OnBack;

        public bool IsOpen => root != null && root.activeSelf;

        /// <summary>Whether the session now forming was started by a ranked search. Reset every time
        /// the panel opens, set only by <see cref="StartRankedSearch"/>, so it can never go stale onto
        /// a casual or friend match. GameFlow reads it when the match begins to decide ladder scoring.</summary>
        public bool RankedSearch { get; private set; }

        public void Build(Transform canvasRoot)
        {
            root = UIFactory.Child(canvasRoot, "OnlineMenu");
            UIFactory.Stretch(UIFactory.Rt(root));
            group = root.AddComponent<CanvasGroup>();

            // Shared translucent dim, matching every other secondary screen. See UIFactory.ScrimDim.
            UIFactory.ScrimDim(root.transform);

            UIFactory.ScreenHeader(root.transform, "Online", Back);

            // The host's code, framed on its own in the middle of the screen while they wait.
            var panel = UIFactory.Panel(root.transform, "OnlinePanel");
            panelRoot = panel;
            var prt = UIFactory.Rt(panel);
            prt.anchorMin = new Vector2(0.5f, 0.5f);
            prt.anchorMax = new Vector2(0.5f, 0.5f);
            prt.pivot = new Vector2(0.5f, 0.5f);
            prt.sizeDelta = new Vector2(560f, 460f);
            prt.anchoredPosition = new Vector2(0f, -10f);

            BuildChoose(root.transform);
            BuildWaiting(panel.transform);
            BuildSearching(root.transform);
            BuildStatus(root.transform);

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

        /// <summary>Space kept clear under the choices for the status line.</summary>
        private const float ChooseBottom = 110f;

        /// <summary>Width of the join panel on the right.</summary>
        private const float JoinWidth = 440f;

        /// <summary>
        /// The ways in, laid out by weight: Ranked as the large card with the screen's one gold call
        /// to action, the two casual ways to play as cards under it, and joining a friend's table in
        /// its own panel on the right — it is a different kind of thing (you need a code from
        /// someone), and a divider in a column of buttons did not say so strongly enough.
        /// </summary>
        private void BuildChoose(Transform parent)
        {
            chooseGroup = UIFactory.Child(parent, "ChooseGroup");
            float top = ArcadeTheme.Xl + ArcadeTheme.HeaderHeight + ArcadeTheme.Xl2;
            UIFactory.Stretch(UIFactory.Rt(chooseGroup), ArcadeTheme.Xl3, ChooseBottom, ArcadeTheme.Xl3, top);

            var h = chooseGroup.AddComponent<HorizontalLayoutGroup>();
            h.spacing = ArcadeTheme.Xl;
            h.childAlignment = TextAnchor.UpperCenter;
            h.childForceExpandWidth = false;
            h.childForceExpandHeight = true;
            h.childControlWidth = true;
            h.childControlHeight = true;

            var play = UIFactory.Child(chooseGroup.transform, "Play");
            play.AddComponent<LayoutElement>().flexibleWidth = 1f;
            var v = play.AddComponent<VerticalLayoutGroup>();
            v.spacing = ArcadeTheme.Xl;
            v.childForceExpandWidth = true;
            v.childForceExpandHeight = false;
            v.childControlWidth = true;
            v.childControlHeight = true;

            rankedButton = UIFactory.ActionCard(play.transform, "Ranked",
                                                "Play for your rank against another player online.",
                                                UIFactory.Icon.Trophy, Ranked, "Find Match");
            var rle = rankedButton.GetComponent<LayoutElement>();
            rle.preferredHeight = 300f;
            rle.flexibleHeight = 1f;

            var casual = UIFactory.Child(play.transform, "Casual");
            var cle = casual.AddComponent<LayoutElement>();
            cle.preferredHeight = 230f;
            cle.flexibleHeight = 1f;
            var ch = casual.AddComponent<HorizontalLayoutGroup>();
            ch.spacing = ArcadeTheme.Xl;
            ch.childForceExpandWidth = true;
            ch.childForceExpandHeight = true;
            ch.childControlWidth = true;
            ch.childControlHeight = true;

            quickButton = UIFactory.ActionCard(casual.transform, "Quick Match",
                                               "Play whoever is searching right now.",
                                               UIFactory.Icon.Dice, QuickMatch);
            hostButton = UIFactory.ActionCard(casual.transform, "Host Table",
                                              "Open a private table and share its code.",
                                              UIFactory.Icon.Table, Host);

            BuildJoin(chooseGroup.transform);
        }

        private void BuildJoin(Transform parent)
        {
            var panel = UIFactory.Panel(parent, "JoinPanel");
            var le = panel.AddComponent<LayoutElement>();
            le.preferredWidth = JoinWidth;
            le.flexibleWidth = 0f;

            var fill = panel.transform.Find("Fill");
            var v = fill.gameObject.AddComponent<VerticalLayoutGroup>();
            int pad = (int)ArcadeTheme.Xl2;
            v.padding = new RectOffset(pad, pad, pad, pad);
            v.spacing = ArcadeTheme.Lg;
            v.childAlignment = TextAnchor.UpperCenter;
            v.childForceExpandWidth = true;
            v.childForceExpandHeight = false;
            v.childControlWidth = true;
            v.childControlHeight = true;

            var title = UIFactory.Text(fill, "Join a table", ArcadeTheme.FsButton * 1.1f, ArcadeTheme.Ink,
                                       display: true, bold: true, upper: true, tracking: 4f,
                                       align: TextAlignmentOptions.Left);
            title.gameObject.AddComponent<LayoutElement>().preferredHeight = ArcadeTheme.FsButton * 1.5f;

            var hint = UIFactory.Text(fill, "Got a code from a friend who is hosting? Enter it here.",
                                      ArcadeTheme.FsBody * 0.9f, ArcadeTheme.InkMuted, display: false,
                                      bold: false, align: TextAlignmentOptions.TopLeft);
            hint.textWrappingMode = TextWrappingModes.Normal;
            hint.gameObject.AddComponent<LayoutElement>().preferredHeight = ArcadeTheme.FsBody * 2.6f;

            codeField = UIFactory.CodeInput(fill, "code", CodeLength);
            joinButton = UIFactory.Button(fill, "Join", MenuButton.Variant.Blue, Join);
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
            rt.anchoredPosition = new Vector2(0f, (ChooseBottom - 40f) * 0.5f);
        }

        // ---------- state ----------

        public void Open()
        {
            if (root == null)
            {
                return;
            }

            opponentArrived = false;
            RankedSearch = false; // casual until a ranked search says otherwise
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

            // Working keeps the choices on screen, switched off, so the player can still see what they
            // asked for while it happens.
            if (chooseGroup != null) chooseGroup.SetActive(choosing || next == Screen.Working);
            if (waitingGroup != null) waitingGroup.SetActive(next == Screen.Waiting);
            if (searchingGroup != null) searchingGroup.SetActive(searching);

            // The code panel only frames the host's code. Everything else happens on the cards, or,
            // for quick match, around the loader with no panel behind it.
            if (panelRoot != null) panelRoot.SetActive(next == Screen.Waiting);

            bool usable = choosing;
            if (rankedButton != null) rankedButton.interactable = usable;
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

        /// <summary>Ranked is a different door: the league screen, not a casual table. GameFlow owns
        /// where it leads — this only reports the choice.</summary>
        private void Ranked()
        {
            OnOpenRanked?.Invoke();
        }

        /// <summary>
        /// Starts a ranked search on this screen's connect machinery (the searching spinner, the
        /// player-count wait and the timeout are all shared with quick match). GameFlow calls this
        /// after opening the panel, having flagged the match ranked.
        /// </summary>
        public async void StartRankedSearch()
        {
            RankedSearch = true;
            SetStatus(string.Empty);
            Show(Screen.Searching);

            if (!await OnlineSession.RankedMatchAsync())
            {
                SetStatus(OnlineSession.LastError);
                Show(Screen.Choose);
                return;
            }

            if (OnlineSession.IsHost)
            {
                SetStatus(string.Empty);
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
