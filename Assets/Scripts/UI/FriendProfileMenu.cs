using System;
using System.Threading.Tasks;
using TableFootball.Net;
using TMPro;
using Unity.Services.Friends.Models;
using UnityEngine;
using UnityEngine.UI;

namespace TableFootball.UI
{
    /// <summary>
    /// One friend, looked at properly: their name, whether they are online, their record, and the two
    /// things you can do about it — invite them, or join the table they are already on.
    ///
    /// A separate screen from <see cref="ProfileMenu"/> rather than a mode of it. The two look alike
    /// and share a vocabulary, but they answer different questions — yours is where you change things
    /// about yourself, this is where you act on somebody else — and the buttons have nothing in
    /// common. Folding them together would mean a screen where half the controls are hidden.
    ///
    /// Like every other screen here it reports through callbacks and owns no sequencing.
    /// </summary>
    public class FriendProfileMenu : MonoBehaviour
    {
        private GameObject root;
        private CanvasGroup group;

        private GameObject avatarHolder;
        private TextMeshProUGUI nameText;
        private TextMeshProUGUI presenceText;
        private TextMeshProUGUI statusText;
        private TextMeshProUGUI winsValue;
        private TextMeshProUGUI lossesValue;
        private TextMeshProUGUI rateValue;
        private TextMeshProUGUI recordNote;

        private MenuButton inviteButton;
        private MenuButton joinButton;

        private string memberId = string.Empty;
        private string friendName = string.Empty;

        private bool dirty;

        /// <summary>Raised once this player is in the friend's session, so the flow can take over.</summary>
        public Action OnJoinedFriend;

        /// <summary>Raised after an invite goes out, since sending one opens a table to wait at.</summary>
        public Action OnInviteSent;

        public Action OnBack;

        public bool IsOpen => root != null && root.activeSelf;

        public void Build(Transform canvasRoot)
        {
            root = UIFactory.Child(canvasRoot, "FriendProfileMenu");
            UIFactory.Stretch(UIFactory.Rt(root));
            group = root.AddComponent<CanvasGroup>();

            // Shared translucent dim, matching every other secondary screen. See UIFactory.ScrimDim.
            UIFactory.ScrimDim(root.transform);

            var panel = UIFactory.Panel(root.transform, "FriendPanel");
            var prt = UIFactory.Rt(panel);
            prt.anchorMin = prt.anchorMax = new Vector2(0.5f, 0.5f);
            prt.pivot = new Vector2(0.5f, 0.5f);
            prt.sizeDelta = new Vector2(620f, 580f);
            prt.anchoredPosition = new Vector2(0f, -10f);

            var fill = panel.transform.Find("Fill");
            var column = UIFactory.Child(fill, "Column");
            UIFactory.Stretch(UIFactory.Rt(column), 32f, 22f, 32f, 22f);

            var v = column.AddComponent<VerticalLayoutGroup>();
            v.spacing = ArcadeTheme.Sm;
            v.childAlignment = TextAnchor.UpperCenter;
            v.childForceExpandWidth = true;
            v.childForceExpandHeight = false;
            v.childControlWidth = true;
            v.childControlHeight = true;

            BuildIdentity(column.transform);
            UIFactory.Spacer(column.transform, 4f);
            UIFactory.SectionRule(column.transform, "online record");
            BuildRecord(column.transform);
            UIFactory.Spacer(column.transform, 6f);
            BuildActions(column.transform);

            BuildStatus(root.transform);
            BuildBack(root.transform);

            root.SetActive(false);
        }

        /// <summary>The avatar, the name, and one line saying whether they can play right now.</summary>
        private void BuildIdentity(Transform parent)
        {
            // Rebuilt per friend, since the disc is drawn around their initial and their colour.
            avatarHolder = UIFactory.Child(parent, "AvatarHolder");
            avatarHolder.AddComponent<LayoutElement>().preferredHeight = 78f;

            var h = avatarHolder.AddComponent<HorizontalLayoutGroup>();
            h.childAlignment = TextAnchor.MiddleCenter;
            h.childForceExpandWidth = false;
            h.childForceExpandHeight = false;
            h.childControlWidth = true;
            h.childControlHeight = true;

            // The whole screen is about one other player, and this is the name they chose. Drawn as
            // text: at this size a markup tag would have the run of the panel.
            nameText = UIFactory.Text(parent, "—", ArcadeTheme.FsTitle * 0.72f, ArcadeTheme.Ink,
                                      display: true, bold: true, upper: false, tracking: 2f,
                                      richText: false);
            nameText.gameObject.AddComponent<LayoutElement>().preferredHeight = 44f;

            presenceText = UIFactory.Text(parent, string.Empty, ArcadeTheme.FsCaption,
                                          ArcadeTheme.InkMuted, display: false, bold: true,
                                          upper: true, tracking: 6f);
            presenceText.gameObject.AddComponent<LayoutElement>().preferredHeight = 20f;
        }

        private void BuildRecord(Transform parent)
        {
            var row = UIFactory.Child(parent, "RecordRow");
            row.AddComponent<LayoutElement>().preferredHeight = 76f;

            var h = row.AddComponent<HorizontalLayoutGroup>();
            h.childAlignment = TextAnchor.MiddleCenter;
            h.spacing = ArcadeTheme.Sm;
            h.childForceExpandWidth = true;
            h.childForceExpandHeight = false;
            h.childControlWidth = true;
            h.childControlHeight = true;

            winsValue = UIFactory.StatBlock(row.transform, "won", ArcadeTheme.Go);
            lossesValue = UIFactory.StatBlock(row.transform, "lost", ArcadeTheme.Red);
            rateValue = UIFactory.StatBlock(row.transform, "win rate", ArcadeTheme.Gold);

            recordNote = UIFactory.Text(parent, string.Empty, ArcadeTheme.FsCaption * 0.9f,
                                        ArcadeTheme.InkMuted, display: false, bold: true,
                                        upper: true, tracking: 2f);
            recordNote.gameObject.AddComponent<LayoutElement>().preferredHeight = 20f;
        }

        private void BuildActions(Transform parent)
        {
            // Join above Invite, because when both are possible joining is the one that starts a game
            // this second while inviting only asks. When they are not on a table, Join is not drawn at
            // all and Invite moves up into the primary slot on its own.
            joinButton = UIFactory.Button(parent, "Join Their Table", MenuButton.Variant.Primary,
                                          Join, 56f);
            inviteButton = UIFactory.Button(parent, "Invite To Play", MenuButton.Variant.Primary,
                                            Invite, 56f);

            // Removing lives here rather than on the list row. It is the one irreversible thing you
            // can do to a friend, and a row that carries it next to Invite puts "play with them" and
            // "delete them" one thumb-width apart in a scrolling list.
            UIFactory.Spacer(parent, 2f);
            UIFactory.Button(parent, "Remove Friend", MenuButton.Variant.Danger, Remove, 48f);
        }

        private void BuildStatus(Transform parent)
        {
            statusText = UIFactory.Text(parent, string.Empty, ArcadeTheme.FsBody, ArcadeTheme.Red,
                                        display: false, bold: true, upper: true, tracking: 3f);
            var rt = UIFactory.Rt(statusText.gameObject);
            rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 0f);
            rt.pivot = new Vector2(0.5f, 0f);
            rt.sizeDelta = new Vector2(760f, 40f);
            rt.anchoredPosition = new Vector2(0f, 142f);
        }

        private void BuildBack(Transform parent)
        {
            var holder = UIFactory.Child(parent, "BackHolder");
            var rt = UIFactory.Rt(holder);
            rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 0f);
            rt.pivot = new Vector2(0.5f, 0f);
            rt.sizeDelta = new Vector2(260f, 58f);
            rt.anchoredPosition = new Vector2(0f, 72f);

            var layout = holder.AddComponent<VerticalLayoutGroup>();
            layout.childForceExpandWidth = true;
            layout.childForceExpandHeight = false;
            layout.childControlWidth = true;
            layout.childControlHeight = true;

            UIFactory.Button(holder.transform, "Back", MenuButton.Variant.Ghost, Back, 58f);
        }

        // ---------- state ----------

        /// <summary>Opens on one friend. Everything shown is re-read from the live list, not passed in,
        /// so it keeps following them while the screen is up.</summary>
        public async void Open(string friendMemberId)
        {
            if (root == null)
            {
                return;
            }

            memberId = friendMemberId ?? string.Empty;
            SetStatus(string.Empty);

            root.SetActive(true);
            root.transform.SetAsLastSibling();
            group.blocksRaycasts = true;

            FriendsHub.OnChanged -= MarkDirty;
            FriendsHub.OnChanged += MarkDirty;

            Redraw();
            await LoadRecordAsync();
        }

        public void Close()
        {
            FriendsHub.OnChanged -= MarkDirty;

            if (root != null)
            {
                group.blocksRaycasts = false;
                root.SetActive(false);
            }
        }

        private void OnDestroy() => FriendsHub.OnChanged -= MarkDirty;

        private void MarkDirty() => dirty = true;

        private void Update()
        {
            if (dirty && IsOpen)
            {
                dirty = false;
                Redraw();
            }
        }

        private Relationship FindFriend()
        {
            foreach (Relationship friend in FriendsHub.Friends)
            {
                if (friend?.Member?.Id == memberId)
                {
                    return friend;
                }
            }

            return null;
        }

        private void Redraw()
        {
            Relationship friend = FindFriend();

            // Removed while their screen was open — from here or from the other device. Backing out is
            // the only sensible thing left, and it beats showing a profile for somebody who is gone.
            //
            // Gated on the hub being ready: an empty list because the service has not answered yet is
            // not the same fact as an empty list because they are no longer a friend, and bouncing
            // straight back out on the first would make the row look like it did nothing.
            if (friend == null)
            {
                if (FriendsHub.IsReady)
                {
                    Back();
                }

                return;
            }

            friendName = FriendsHub.NameOf(friend);
            bool online = FriendsHub.IsOnline(friend);
            string joinCode = FriendsHub.JoinCodeFor(friend);

            nameText.text = friendName;

            presenceText.text = online
                ? (string.IsNullOrEmpty(joinCode) ? "online" : "at a table — waiting for a player")
                : "offline";
            presenceText.color = online ? ArcadeTheme.Go : ArcadeTheme.InkMuted;

            RebuildAvatar(online);

            // Only offered when it would actually do something. Both greyed out on an offline friend
            // would be a screen made mostly of things you cannot press.
            joinButton.gameObject.SetActive(!string.IsNullOrEmpty(joinCode));
            inviteButton.gameObject.SetActive(online);
            inviteButton.Configure(string.IsNullOrEmpty(joinCode)
                ? MenuButton.Variant.Primary
                : MenuButton.Variant.Neutral);

            // No status line for an absent friend. The presence text under their name already says
            // "offline" and both actions have removed themselves — saying it a third time in red
            // reads as an error the player caused rather than as a fact about somebody else's evening.
        }

        private void RebuildAvatar(bool online)
        {
            if (avatarHolder == null)
            {
                return;
            }

            UIFactory.ClearChildren(avatarHolder.transform);
            UIFactory.AvatarDisc(avatarHolder.transform, friendName,
                                 online ? ArcadeTheme.Go : ArcadeTheme.InkMuted, 68f);
        }

        /// <summary>
        /// Fills in the record, or says plainly that it cannot be read.
        ///
        /// A friend's record has to come from somewhere both devices can see, which is the leaderboard
        /// service — and that is not wired up yet. Rather than show three zeroes, which would read as
        /// "they have never won a game", the blocks stay blank and the note underneath says why.
        /// </summary>
        private async Task LoadRecordAsync()
        {
            ShowRecord(null);

            if (!LeaderboardHub.IsAvailable)
            {
                recordNote.text = "records are not being kept yet";
                return;
            }

            LeaderboardHub.PlayerRecord? record = await LeaderboardHub.TryGetRecordAsync(memberId);

            if (!IsOpen)
            {
                return;
            }

            ShowRecord(record);
            recordNote.text = record.HasValue ? string.Empty : "they have not played an online match yet";
        }

        private void ShowRecord(LeaderboardHub.PlayerRecord? record)
        {
            if (!record.HasValue)
            {
                winsValue.text = "–";
                lossesValue.text = "–";
                rateValue.text = "–";
                return;
            }

            int wins = record.Value.Wins;
            int losses = record.Value.Losses;
            int played = wins + losses;

            winsValue.text = wins.ToString();
            lossesValue.text = losses.ToString();
            rateValue.text = played > 0 ? $"{Mathf.RoundToInt(100f * wins / played)}%" : "–";
        }

        private void SetStatus(string message)
        {
            if (statusText != null)
            {
                statusText.text = message ?? string.Empty;
            }
        }

        // ---------- actions ----------

        private async void Invite()
        {
            SetStatus("OPENING A TABLE…");

            if (await FriendsHub.SendInviteAsync(memberId))
            {
                Close();
                OnInviteSent?.Invoke();
            }
            else
            {
                SetStatus(FriendsHub.LastError);
            }
        }

        private async void Join()
        {
            Relationship friend = FindFriend();
            string joinCode = FriendsHub.JoinCodeFor(friend);

            if (string.IsNullOrEmpty(joinCode))
            {
                SetStatus("THEY ARE NOT AT A TABLE ANY MORE");
                Redraw();
                return;
            }

            SetStatus("JOINING…");

            if (await OnlineSession.JoinAsync(joinCode))
            {
                Close();
                OnJoinedFriend?.Invoke();
            }
            else
            {
                // Most often their table filled up between the presence update and the tap.
                SetStatus(OnlineSession.LastError);
                Redraw();
            }
        }

        private async void Remove()
        {
            SetStatus("REMOVING…");

            if (await FriendsHub.RemoveAsync(memberId))
            {
                // Redraw would send us back anyway once the list drops them, but going straight out
                // is what the player asked for and avoids a frame of their now-stale profile.
                Back();
            }
            else
            {
                SetStatus(FriendsHub.LastError);
            }
        }

        private void Back()
        {
            Close();
            OnBack?.Invoke();
        }
    }
}
