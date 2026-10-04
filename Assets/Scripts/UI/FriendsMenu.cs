using System;
using System.Threading.Tasks;
using TableFootball.Net;
using TableFootball.UI.Toolkit;
using Unity.Services.Friends.Models;
using UnityEngine;
using UnityEngine.UIElements;

namespace TableFootball.UI
{
    /// <summary>
    /// The friends list: your own code to hand out and a field to add someone by theirs (left), and
    /// everyone you know (right) — an invite waiting on you first, then requests, then friends. Built on
    /// UI Toolkit, styled by <c>Resources/UI/Styles/Friends.uss</c>.
    ///
    /// The right side reads top to bottom in order of urgency. An invitation to play expires, so it is a
    /// banner above the list rather than a row in it, and requests come before friends because somebody
    /// is waiting on an answer. Rows are rebuilt wholesale on every change rather than diffed: a
    /// foosball player has friends in the dozens, not the thousands, and a rebuild that cannot drift
    /// out of step with the service is worth more here than a few dozen saved allocations.
    ///
    /// Joining reuses <see cref="OnlineSession.JoinAsync"/> untouched — a friend's row simply carries the
    /// code their presence is advertising, so joining from here and typing a code into
    /// <see cref="OnlineMenu"/> are the same operation with the typing removed.
    /// </summary>
    public class FriendsMenu : MonoBehaviour
    {
        private VisualElement root;
        private VisualElement inviteBanner;
        private Label inviteText;
        private Label myInitial, copyLabel, statusLabel, onlineLabel;
        private UiShineLabel myCode;
        private TextField addField;
        private VisualElement addFrame;
        private ScrollView list;

        private int statusToken;
        private int copyToken;

        /// <summary>Set from the service callback, acted on in <see cref="Update"/>.</summary>
        private bool dirty;

        /// <summary>Raised when the player joins a friend's table, so the flow can start the match.</summary>
        public Action OnJoinedFriend;

        /// <summary>Raised after an invite goes out — sending one opens a table that needs a screen.</summary>
        public Action OnInviteSent;

        /// <summary>Raised with a friend's member id when their name is tapped.</summary>
        public Action<string> OnOpenFriendProfile;

        public Action OnBack;

        public bool IsOpen => root != null && root.style.display != DisplayStyle.None;

        /// <summary><paramref name="canvasRoot"/> is the uGUI canvas the older screens hang off, unused
        /// here and kept so the boot order in TableFootballUI does not change.</summary>
        public void Build(Transform canvasRoot)
        {
            root = UiKit.El("screen friends", UiToolkitHost.Root, "Friends");
            var sheet = Resources.Load<StyleSheet>("UI/Styles/Friends");
            if (sheet != null) root.styleSheets.Add(sheet);
            root.pickingMode = PickingMode.Position;

            UiBackdrop.AddTo(root);

            BuildHeader();
            BuildSide();
            BuildRight();

            statusLabel = UiKit.Text(string.Empty, "fr__status f-display-semi", root);
            statusLabel.pickingMode = PickingMode.Ignore;

            UiFonts.Apply(root);
            UiKit.Show(root, false);
        }

        // ---------- build ----------

        private void BuildHeader()
        {
            var header = UiKit.El("fr__header", root);

            var back = UiKit.Frame("sq fr__back", header);
            back.Add(new UiIcon(UiIcon.Glyph.ChevronLeft, ArcadeTheme.Ink, 2.5f));
            UiKit.OnTap(back, Back, UiSound.Back);

            var box = UiKit.El("fr__title-box", header);
            var row = UiKit.El("fr__title-row", box);
            row.Add(new UiIcon(UiIcon.Glyph.TwoPlayers, ArcadeTheme.Pulse, 2f));
            UiKit.Text("FRIENDS", "fr__title f-display", row);
            UiKit.El("fr__bar", box);

            var online = UiKit.Frame("fr__online", header);
            UiKit.El("fr__online-dot", online);
            onlineLabel = UiKit.Text("0 ONLINE", "fr__online-label f-display", online);
        }

        private void BuildSide()
        {
            var side = UiKit.El("fr__side", root);

            // Your code: what a friend types to add you.
            var mine = UiKit.Frame("frame--panel fr__card", side);
            UiKit.Text("YOUR CODE", "fr__caption f-display", mine);

            var identity = UiKit.El("fr__me", mine);
            var hex = new UiHex();
            hex.AddToClassList("fr__me-hex");
            myInitial = UiKit.Text("?", "fr__me-initial f-display", hex);
            identity.Add(hex);
            myCode = new UiShineLabel();
            myCode.AddToClassList("fr__code");
            myCode.AddToClassList("f-display");
            identity.Add(myCode);

            var copy = UiKit.Frame("btn btn--blue fr__copy", mine);
            copy.Add(new UiIcon(UiIcon.Glyph.Copy, ArcadeTheme.Pulse, 2f));
            copyLabel = UiKit.Text("COPY CODE", "btn__label f-display", copy);
            UiKit.OnTap(copy, CopyCode);

            // Someone else's code: add them.
            var add = UiKit.Frame("frame--panel fr__card", side);
            UiKit.Text("ADD A FRIEND", "fr__caption f-display", add);

            addFrame = UiKit.Frame("fr__field", add);
            addFrame.Add(new UiIcon(UiIcon.Glyph.AddPerson, ArcadeTheme.Pulse, 2f));
            addField = new TextField { maxLength = 30, autoCorrection = false };
            addField.AddToClassList("fr__field-input");
            addField.AddToClassList("f-body-semi");
            addField.RegisterCallback<FocusInEvent>(_ => addFrame.AddToClassList("is-focus"));
            addField.RegisterCallback<FocusOutEvent>(_ => addFrame.RemoveFromClassList("is-focus"));
            addFrame.Add(addField);

            UiKit.Text("player#1234", "fr__hint f-body-medium", add);
            UiKit.Button("ADD", "pulse", Add, add, "fr__add");
        }

        private void BuildRight()
        {
            var right = UiKit.El("fr__right", root);

            inviteBanner = UiKit.Frame("fr__invite", right);
            ((UiFrame)inviteBanner).OverrideEdge(ArcadeTheme.Reward);
            inviteBanner.Add(new UiIcon(UiIcon.Glyph.Play, ArcadeTheme.Reward, 2.4f));
            // Carries a friend's chosen name, so it is drawn as text and never as markup.
            inviteText = UiKit.Text(string.Empty, "fr__invite-text f-display", inviteBanner);
            UiKit.Button("PLAY", "pulse", AcceptInvite, inviteBanner, "fr__act");
            // Declines rather than dismisses: somebody is sitting at a table waiting on this answer,
            // and clearing the banner without sending one leaves them there.
            UiKit.Button("LATER", "ghost", Decline, inviteBanner, "fr__act");
            UiKit.Show(inviteBanner, false);

            list = new ScrollView(ScrollViewMode.Vertical) { name = "FriendsList" };
            list.AddToClassList("fr__list");
            list.horizontalScrollerVisibility = ScrollerVisibility.Hidden;
            list.verticalScrollerVisibility = ScrollerVisibility.Hidden;
            list.touchScrollBehavior = ScrollView.TouchScrollBehavior.Elastic;
            right.Add(list);
        }

        // ---------- state ----------

        public async void Open()
        {
            if (root == null) return;

            SetStatus(string.Empty, false);
            addField.value = string.Empty;

            UiKit.Show(root, true);
            root.BringToFront();
            root.style.opacity = 0f;
            UiKit.Fade(root, 1f, ArcadeTheme.TFast);
            GameSfx.PlayUi(UiSound.Open);

            FriendsHub.OnChanged -= MarkDirty;
            FriendsHub.OnChanged += MarkDirty;

            RefreshIdentity();
            Rebuild();

            await PlayerAccount.RefreshAsync();
            RefreshIdentity();

            if (!await FriendsHub.EnsureReadyAsync())
            {
                SetStatus(FriendsHub.LastError, true);
            }

            Rebuild();
        }

        public void Close()
        {
            FriendsHub.OnChanged -= MarkDirty;
            if (root != null) UiKit.Show(root, false);
        }

        private void OnDestroy() => FriendsHub.OnChanged -= MarkDirty;

        private void MarkDirty() => dirty = true;

        /// <summary>
        /// Service notifications arrive off whatever thread the SDK pleases and can land several at
        /// once — a presence sweep raises one per friend. Collapsing them into a single rebuild per
        /// frame keeps the list correct without rebuilding it eight times in a row.
        /// </summary>
        private void Update()
        {
            if (dirty && IsOpen)
            {
                dirty = false;
                Rebuild();
            }
        }

        private void RefreshIdentity()
        {
            string name = PlayerAccount.DisplayName;
            bool has = !string.IsNullOrWhiteSpace(name);
            myCode.Set(has ? name : "—");
            myInitial.text = has ? name.Substring(0, 1).ToUpperInvariant() : "?";
        }

        // ---------- the list ----------

        private void Rebuild()
        {
            if (list == null) return;

            list.Clear();
            RefreshInviteBanner();

            int online = 0;
            int rows = 0;

            foreach (Relationship request in FriendsHub.IncomingRequests)
            {
                BuildRequestRow(request);
                rows++;
            }

            foreach (Relationship friend in FriendsHub.Friends)
            {
                if (FriendsHub.IsOnline(friend)) online++;
                BuildFriendRow(friend);
                rows++;
            }

            onlineLabel.text = online + " ONLINE";

            if (rows == 0) BuildEmptyState();

            UiFonts.Apply(list);
        }

        /// <summary>
        /// What fills the list before you know anyone — the first thing a new player sees here. Two
        /// overlapping hex avatars, a headline and one plain instruction read as "you have not added
        /// anyone yet" rather than as an empty, half-broken screen.
        /// </summary>
        private void BuildEmptyState()
        {
            var empty = UiKit.El("fr__empty", list.contentContainer);

            var art = UiKit.El("fr__empty-art", empty);
            var a = new UiHex { Edge = ArcadeTheme.Blue };
            a.AddToClassList("fr__empty-hex");
            a.AddToClassList("fr__empty-hex--l");
            a.Add(new UiIcon(UiIcon.Glyph.Person, ArcadeTheme.Blue, 2f));
            art.Add(a);
            var b = new UiHex { Edge = ArcadeTheme.Pulse };
            b.AddToClassList("fr__empty-hex");
            b.AddToClassList("fr__empty-hex--r");
            b.Add(new UiIcon(UiIcon.Glyph.Person, ArcadeTheme.Pulse, 2f));
            art.Add(b);

            UiKit.Text("NO FRIENDS YET", "fr__empty-head f-display", empty);
            UiKit.Text("SHARE YOUR CODE, OR ADD A FRIEND BY THEIRS", "fr__empty-line f-display-semi", empty);
        }

        private void RefreshInviteBanner()
        {
            FriendsHub.Invite? invite = FriendsHub.PendingInvite;
            UiKit.Show(inviteBanner, invite.HasValue);

            if (invite.HasValue)
            {
                inviteText.text = invite.Value.FromName.ToUpperInvariant() + " WANTS TO PLAY";
            }
        }

        private UiFrame RowShell()
        {
            return UiKit.Frame("fr__row", list.contentContainer);
        }

        private static VisualElement Identity(VisualElement row, string name, string subtitle, Color tint)
        {
            var identity = UiKit.El("fr__id", row);

            var hex = new UiHex { Edge = tint };
            hex.AddToClassList("fr__hex");
            string initial = string.IsNullOrEmpty(name) ? "?" : name.Substring(0, 1).ToUpperInvariant();
            var letter = UiKit.Text(initial, "fr__initial f-display", hex);
            letter.style.color = tint;
            identity.Add(hex);

            var words = UiKit.El("fr__words", identity);
            UiKit.Text(name, "fr__name f-display", words);
            var sub = UiKit.Text(subtitle, "fr__sub f-display-semi", words);
            sub.style.color = tint == ArcadeTheme.Reward ? ArcadeTheme.Reward : ArcadeTheme.InkMuted;
            return identity;
        }

        private void BuildRequestRow(Relationship request)
        {
            var row = RowShell();
            row.OverrideEdge(ArcadeTheme.Reward);
            string id = request.Member.Id;

            // Not tappable through to a profile: there is no friendship yet, so there would be nothing
            // on the other side but a name you already have and two buttons already on this row.
            Identity(row, FriendsHub.NameOf(request), "WANTS TO BE FRIENDS", ArcadeTheme.Reward);

            UiKit.Button("ACCEPT", "pulse", async () => await Run(FriendsHub.AcceptAsync(id)), row, "fr__act");

            var no = UiKit.Frame("sq fr__no", row);
            no.Add(new UiIcon(UiIcon.Glyph.Close, ArcadeTheme.InkMuted, 2.4f));
            UiKit.OnTap(no, async () => await Run(FriendsHub.DeclineAsync(id)), UiSound.Back);
        }

        private void BuildFriendRow(Relationship friend)
        {
            string id = friend.Member.Id;
            bool online = FriendsHub.IsOnline(friend);
            string joinCode = FriendsHub.JoinCodeFor(friend);
            bool atTable = online && !string.IsNullOrEmpty(joinCode);

            var row = RowShell();
            if (!online) row.AddToClassList("is-offline");
            else row.OverrideEdge(new Color(ArcadeTheme.Go.r, ArcadeTheme.Go.g, ArcadeTheme.Go.b, 0.55f));

            // Online friends get a green bar down the left edge.
            if (online)
            {
                var bar = UiKit.El("fr__bar-online", row);
                bar.pickingMode = PickingMode.Ignore;
            }

            // A level in front of the status, the way a real multiplayer roster reads. Placeholder —
            // see PlayerProgress.MockLevelFor — but stable per friend, so it does not flicker.
            string status = !online ? "OFFLINE" : atTable ? "AT A TABLE" : "ONLINE";
            string subtitle = "LV " + PlayerProgress.MockLevelFor(id) + "  ·  " + status;
            Color tint = online ? ArcadeTheme.Go : ArcadeTheme.InkMuted;

            var identity = Identity(row, FriendsHub.NameOf(friend), subtitle, tint);
            UiKit.OnTap(identity, () => OpenProfile(id));

            // A presence dot on every row: lit when online, a static grey otherwise, so "no dot" never
            // has to be read as its own kind of status.
            var dot = UiKit.El("fr__dot", row);
            dot.style.backgroundColor = tint;
            dot.pickingMode = PickingMode.Ignore;

            if (atTable)
            {
                UiKit.Button("JOIN", "blue", () => Join(joinCode), row, "fr__act");
            }
            else if (online)
            {
                UiKit.Button("INVITE", "pulse", () => InviteFriend(id), row, "fr__act");
            }
        }

        // ---------- actions ----------

        private void OpenProfile(string friendMemberId)
        {
            Close();
            OnOpenFriendProfile?.Invoke(friendMemberId);
        }

        private void CopyCode()
        {
            string code = PlayerAccount.DisplayName;
            if (string.IsNullOrWhiteSpace(code))
            {
                SetStatus("NO CODE YET · TRY AGAIN IN A MOMENT", true);
                return;
            }

            GUIUtility.systemCopyBuffer = code;
            copyLabel.text = "COPIED";
            int token = ++copyToken;
            copyLabel.schedule.Execute(() =>
            {
                if (token == copyToken) copyLabel.text = "COPY CODE";
            }).StartingIn(1600);
        }

        private async void Add()
        {
            SetStatus("SENDING…", false);

            if (await FriendsHub.SendRequestAsync(addField.value))
            {
                addField.value = string.Empty;
                SetStatus("REQUEST SENT", false);
                GameSfx.PlayUi(UiSound.Confirm);
                Rebuild();
            }
            else
            {
                SetStatus(FriendsHub.LastError, true);
            }
        }

        private async void InviteFriend(string memberId)
        {
            SetStatus("OPENING A TABLE…", false);

            if (await FriendsHub.SendInviteAsync(memberId))
            {
                Close();
                OnInviteSent?.Invoke();
            }
            else
            {
                SetStatus(FriendsHub.LastError, true);
            }
        }

        private async void AcceptInvite()
        {
            FriendsHub.Invite? invite = FriendsHub.PendingInvite;
            if (!invite.HasValue) return;

            // Cleared before joining, not after. Whether the join succeeds or the table has since
            // filled, the invitation has been answered and should stop asking.
            string code = invite.Value.JoinCode;
            FriendsHub.DismissInvite();

            await JoinCode(code);
        }

        private async void Decline()
        {
            await FriendsHub.DeclineInviteAsync();
            Rebuild();
        }

        private async void Join(string joinCode) => await JoinCode(joinCode);

        private async Task JoinCode(string joinCode)
        {
            SetStatus("JOINING…", false);

            if (await OnlineSession.JoinAsync(joinCode))
            {
                Close();
                OnJoinedFriend?.Invoke();
            }
            else
            {
                // Most often their table filled up between the presence update and the tap.
                SetStatus(OnlineSession.LastError, true);
                Rebuild();
            }
        }

        /// <summary>Runs a hub call, reports whatever it said went wrong, and redraws either way.</summary>
        private async Task Run(Task<bool> call)
        {
            if (!await call)
            {
                SetStatus(FriendsHub.LastError, true);
            }

            Rebuild();
        }

        /// <summary>One line under the list: what just happened, in cyan, or what went wrong, in red.
        /// Cleared after a few seconds so it never lingers as if it were still true.</summary>
        private void SetStatus(string message, bool error)
        {
            if (statusLabel == null) return;

            statusLabel.text = (message ?? string.Empty).ToUpperInvariant();
            statusLabel.style.color = error ? ArcadeTheme.Danger : ArcadeTheme.Pulse;
            if (error && !string.IsNullOrEmpty(message)) GameSfx.PlayUi(UiSound.Deny);

            int token = ++statusToken;
            statusLabel.schedule.Execute(() =>
            {
                if (token == statusToken) statusLabel.text = string.Empty;
            }).StartingIn(4000);
        }

        private void Back()
        {
            Close();
            OnBack?.Invoke();
        }
    }
}
