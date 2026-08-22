using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using TableFootball.Net;
using TMPro;
using Unity.Services.Friends.Models;
using UnityEngine;
using UnityEngine.UI;

namespace TableFootball.UI
{
    /// <summary>
    /// The friends list: an invite waiting on you, your own code to hand out, and everyone you know.
    ///
    /// The screen reads top to bottom in order of urgency — an invite is answered now, your code is
    /// read out occasionally, the list is browsed. The invite banner is therefore not a row in the
    /// list: an invitation to play expires, and burying it among people who are merely online would
    /// be the one thing on this screen with a deadline hidden among the things without one.
    ///
    /// Rows are rebuilt wholesale on every change rather than diffed. A foosball player has friends in
    /// the dozens, not the thousands, and a rebuild that cannot drift out of step with the service is
    /// worth more here than one that saves a few dozen allocations on a menu screen.
    ///
    /// Joining reuses <see cref="OnlineSession.JoinAsync"/> untouched — a friend's row simply carries
    /// the code their presence is advertising, so joining from here and typing a code into
    /// <see cref="OnlineMenu"/> are the same operation with the typing removed.
    /// </summary>
    public class FriendsMenu : MonoBehaviour
    {
        private GameObject root;
        private CanvasGroup group;

        private GameObject inviteBanner;
        private TextMeshProUGUI inviteText;
        private GameObject myAvatarHolder;
        private TextMeshProUGUI myCode;
        private TextMeshProUGUI statusText;
        private TMP_InputField addField;
        private RectTransform listContent;

        private readonly List<GameObject> rows = new();

        /// <summary>Set from the service callback, acted on in <see cref="Update"/>.</summary>
        private bool dirty;

        /// <summary>Raised when the player joins a friend's table, so the flow can start the match.</summary>
        public Action OnJoinedFriend;

        /// <summary>Raised after an invite goes out — sending one opens a table that needs a screen.</summary>
        public Action OnInviteSent;

        /// <summary>Raised with a friend's member id when their row is tapped.</summary>
        public Action<string> OnOpenFriendProfile;

        /// <summary>Raised by the account button — the profile screen is its own panel.</summary>
        public Action OnOpenProfile;

        public Action OnBack;

        public bool IsOpen => root != null && root.activeSelf;

        public void Build(Transform canvasRoot)
        {
            root = UIFactory.Child(canvasRoot, "FriendsMenu");
            UIFactory.Stretch(UIFactory.Rt(root));
            group = root.AddComponent<CanvasGroup>();

            UIFactory.Backdrop(root.transform);

            var title = UIFactory.Text(root.transform, "FRIENDS", ArcadeTheme.FsTitle, ArcadeTheme.Ink,
                                       display: true, bold: true, upper: true, tracking: 8f);
            var trt = UIFactory.Rt(title.gameObject);
            trt.anchorMin = new Vector2(0f, 1f);
            trt.anchorMax = new Vector2(1f, 1f);
            trt.pivot = new Vector2(0.5f, 1f);
            trt.offsetMin = new Vector2(0f, -118f);
            trt.offsetMax = new Vector2(0f, -46f);

            // One column holding everything, so the banner appearing pushes the rest down instead of
            // covering it — the old fixed-position panels had nowhere for a banner to go.
            var column = UIFactory.Child(root.transform, "Column");
            var crt = UIFactory.Rt(column);
            // Stretched to the screen with a margin, rather than set to a fixed width.
            //
            // Landscape is where the room is, so this wants to be wide — but how wide is not knowable
            // from here: the canvas is about 1790 reference units across at 20:9 and only about 1385
            // at 4:3, and any number picked for one of those overflows the other. Anchoring to both
            // edges makes the margin the constant instead of the width, so it fills whatever it is
            // given and can never run off the side.
            crt.anchorMin = new Vector2(0f, 0f);
            crt.anchorMax = new Vector2(1f, 1f);
            crt.pivot = new Vector2(0.5f, 1f);
            crt.offsetMin = new Vector2(220f, 148f);
            crt.offsetMax = new Vector2(-220f, -128f);

            var v = column.AddComponent<VerticalLayoutGroup>();
            v.spacing = ArcadeTheme.Sm;
            v.childAlignment = TextAnchor.UpperCenter;
            v.childForceExpandWidth = true;
            v.childForceExpandHeight = false;
            v.childControlWidth = true;
            v.childControlHeight = true;

            BuildInviteBanner(column.transform);
            BuildHeader(column.transform);
            BuildList(column.transform);

            BuildStatus(root.transform);
            BuildBack(root.transform);

            root.SetActive(false);
        }

        /// <summary>
        /// The invitation, when there is one. Gold-bordered and top of the screen, because it is the
        /// only thing here that somebody else is waiting on an answer to.
        /// </summary>
        private void BuildInviteBanner(Transform parent)
        {
            var panel = UIFactory.Panel(parent, "InviteBanner");
            inviteBanner = panel;
            panel.AddComponent<LayoutElement>().preferredHeight = 92f;

            var border = panel.transform.Find("Border");
            if (border != null) border.GetComponent<Image>().color = ArcadeTheme.Gold;

            var fill = panel.transform.Find("Fill");
            var row = UIFactory.Child(fill, "Row");
            UIFactory.Stretch(UIFactory.Rt(row), 18f, 14f, 18f, 14f);

            var h = row.AddComponent<HorizontalLayoutGroup>();
            h.spacing = ArcadeTheme.Sm;
            h.childAlignment = TextAnchor.MiddleLeft;
            h.childForceExpandWidth = false;
            h.childForceExpandHeight = true;
            h.childControlWidth = true;
            h.childControlHeight = true;

            // Carries a friend's chosen name, so it is drawn as text and not as markup.
            inviteText = UIFactory.Text(row.transform, string.Empty, ArcadeTheme.FsCaption,
                                        ArcadeTheme.Gold, display: false, bold: true, upper: true,
                                        tracking: 3f, align: TextAlignmentOptions.Left,
                                        richText: false);
            inviteText.gameObject.AddComponent<LayoutElement>().flexibleWidth = 1f;

            var accept = UIFactory.Button(row.transform, "Play", MenuButton.Variant.Primary,
                                          AcceptInvite, 48f);
            accept.gameObject.GetComponent<LayoutElement>().preferredWidth = 150f;

            // Declines rather than dismisses: somebody is sitting at a table waiting on this answer,
            // and clearing the banner without sending one leaves them there.
            var dismiss = UIFactory.Button(row.transform, "Later", MenuButton.Variant.Ghost,
                                           Decline, 48f);
            dismiss.gameObject.GetComponent<LayoutElement>().preferredWidth = 130f;

            inviteBanner.SetActive(false);
        }

        /// <summary>
        /// Your own identity and the add field. Above the list because both are things you do once on
        /// arriving, while the list is what you came to read.
        /// </summary>
        private void BuildHeader(Transform parent)
        {
            var panel = UIFactory.Panel(parent, "MyCodePanel");
            panel.AddComponent<LayoutElement>().preferredHeight = 168f;

            var fill = panel.transform.Find("Fill");
            var column = UIFactory.Child(fill, "HeaderColumn");
            UIFactory.Stretch(UIFactory.Rt(column), 18f, 14f, 18f, 14f);

            var layout = column.AddComponent<VerticalLayoutGroup>();
            layout.spacing = ArcadeTheme.Sm;
            layout.childAlignment = TextAnchor.UpperCenter;
            layout.childForceExpandWidth = true;
            layout.childForceExpandHeight = false;
            layout.childControlWidth = true;
            layout.childControlHeight = true;

            // Your own row, built like a friend's: avatar, name, and the caption under it saying what
            // the name is FOR. Sharing the shape is what makes "this is me, those are them" obvious
            // without either being labelled.
            var identity = UIFactory.Child(column.transform, "MyIdentity");
            identity.AddComponent<LayoutElement>().preferredHeight = 60f;
            var ih = identity.AddComponent<HorizontalLayoutGroup>();
            ih.spacing = ArcadeTheme.Md;
            ih.childAlignment = TextAnchor.MiddleLeft;
            ih.childForceExpandWidth = false;
            ih.childForceExpandHeight = false;
            ih.childControlWidth = true;
            ih.childControlHeight = true;

            myAvatarHolder = UIFactory.Child(identity.transform, "AvatarHolder");
            var ale = myAvatarHolder.AddComponent<LayoutElement>();
            ale.preferredWidth = 48f;
            ale.preferredHeight = 48f;

            var textCol = UIFactory.Child(identity.transform, "Text");
            textCol.AddComponent<LayoutElement>().flexibleWidth = 1f;
            var tv = textCol.AddComponent<VerticalLayoutGroup>();
            tv.spacing = 0f;
            tv.childAlignment = TextAnchor.MiddleLeft;
            tv.childForceExpandWidth = true;
            tv.childForceExpandHeight = false;
            tv.childControlWidth = true;
            tv.childControlHeight = true;

            myCode = UIFactory.Text(textCol.transform, "—", ArcadeTheme.FsBody, ArcadeTheme.Gold,
                                    display: true, bold: true, upper: false, tracking: 2f,
                                    align: TextAlignmentOptions.Left, richText: false);
            myCode.gameObject.AddComponent<LayoutElement>().preferredHeight = 30f;

            var hint = UIFactory.Text(textCol.transform, "your code — share it to be added",
                                      ArcadeTheme.FsCaption * 0.82f, ArcadeTheme.InkMuted,
                                      display: false, bold: true, upper: true, tracking: 3f,
                                      align: TextAlignmentOptions.Left);
            hint.gameObject.AddComponent<LayoutElement>().preferredHeight = 18f;

            var account = UIFactory.Button(identity.transform, "Account", MenuButton.Variant.Ghost,
                                           () => { Close(); OnOpenProfile?.Invoke(); }, 48f);
            account.gameObject.GetComponent<LayoutElement>().preferredWidth = 150f;

            var addRow = UIFactory.Child(column.transform, "AddRow");
            addRow.AddComponent<LayoutElement>().preferredHeight = 52f;
            var rowLayout = addRow.AddComponent<HorizontalLayoutGroup>();
            rowLayout.spacing = ArcadeTheme.Sm;
            rowLayout.childAlignment = TextAnchor.MiddleCenter;
            rowLayout.childForceExpandWidth = false;
            rowLayout.childForceExpandHeight = true;
            rowLayout.childControlWidth = true;
            rowLayout.childControlHeight = true;

            addField = UIFactory.TextInput(addRow.transform, "add by code, e.g. player#1234", 30,
                                           TMP_InputField.CharacterValidation.None, height: 52f);
            addField.gameObject.GetComponent<LayoutElement>().flexibleWidth = 1f;

            var add = UIFactory.Button(addRow.transform, "Add", MenuButton.Variant.Primary, Add, 52f);
            add.gameObject.GetComponent<LayoutElement>().preferredWidth = 150f;
        }

        private void BuildList(Transform parent)
        {
            var panel = UIFactory.Panel(parent, "FriendsListPanel");
            var le = panel.AddComponent<LayoutElement>();
            le.flexibleHeight = 1f;
            le.minHeight = 180f;

            var fill = panel.transform.Find("Fill");
            var holder = UIFactory.Child(fill, "ListHolder");
            UIFactory.Stretch(UIFactory.Rt(holder), 18f, 14f, 18f, 14f);

            listContent = UIFactory.ScrollList(holder.transform);
        }

        private void BuildStatus(Transform parent)
        {
            statusText = UIFactory.Text(parent, string.Empty, ArcadeTheme.FsBody, ArcadeTheme.Red,
                                        display: false, bold: true, upper: true, tracking: 3f);
            var rt = UIFactory.Rt(statusText.gameObject);
            rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 0f);
            rt.pivot = new Vector2(0.5f, 0f);
            rt.sizeDelta = new Vector2(760f, 36f);
            rt.anchoredPosition = new Vector2(0f, 108f);
        }

        private void BuildBack(Transform parent)
        {
            var holder = UIFactory.Child(parent, "BackHolder");
            var rt = UIFactory.Rt(holder);
            rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 0f);
            rt.pivot = new Vector2(0.5f, 0f);
            rt.sizeDelta = new Vector2(260f, 54f);
            rt.anchoredPosition = new Vector2(0f, 44f);

            var layout = holder.AddComponent<VerticalLayoutGroup>();
            layout.childForceExpandWidth = true;
            layout.childForceExpandHeight = false;
            layout.childControlWidth = true;
            layout.childControlHeight = true;

            UIFactory.Button(holder.transform, "Back", MenuButton.Variant.Ghost, Back, 54f);
        }

        // ---------- state ----------

        public async void Open()
        {
            if (root == null)
            {
                return;
            }

            SetStatus(string.Empty);
            addField.text = string.Empty;

            root.SetActive(true);
            root.transform.SetAsLastSibling();
            group.blocksRaycasts = true;

            FriendsHub.OnChanged -= MarkDirty;
            FriendsHub.OnChanged += MarkDirty;

            await PlayerAccount.RefreshAsync();
            RefreshIdentity();

            if (!await FriendsHub.EnsureReadyAsync())
            {
                SetStatus(FriendsHub.LastError);
            }

            Rebuild();
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
            myCode.text = string.IsNullOrEmpty(name) ? "—" : name;

            UIFactory.ClearChildren(myAvatarHolder.transform);
            UIFactory.AvatarDisc(myAvatarHolder.transform, name, ArcadeTheme.Gold, 48f);
        }

        // ---------- the list ----------

        private void Rebuild()
        {
            UIFactory.ClearChildren(listContent);
            rows.Clear();
            RefreshInviteBanner();

            if (listContent == null)
            {
                return;
            }

            foreach (Relationship request in FriendsHub.IncomingRequests)
            {
                BuildRequestRow(request);
            }

            foreach (Relationship friend in FriendsHub.Friends)
            {
                BuildFriendRow(friend);
            }

            if (rows.Count == 0)
            {
                var empty = UIFactory.Text(listContent, "nobody here yet — share your code above",
                                           ArcadeTheme.FsCaption, ArcadeTheme.InkMuted,
                                           display: false, bold: true, upper: true, tracking: 3f);
                empty.gameObject.AddComponent<LayoutElement>().preferredHeight = 44f;
                rows.Add(empty.gameObject);
            }
        }

        private void RefreshInviteBanner()
        {
            FriendsHub.Invite? invite = FriendsHub.PendingInvite;

            if (inviteBanner == null)
            {
                return;
            }

            inviteBanner.SetActive(invite.HasValue);

            if (invite.HasValue)
            {
                inviteText.text = $"{invite.Value.FromName} wants to play";
            }
        }

        private Transform BuildRowShell()
        {
            var row = UIFactory.Child(listContent, "Row");
            row.AddComponent<LayoutElement>().preferredHeight = 70f;

            var layout = row.AddComponent<HorizontalLayoutGroup>();
            layout.spacing = ArcadeTheme.Sm;
            layout.childAlignment = TextAnchor.MiddleLeft;
            layout.childForceExpandWidth = false;
            layout.childForceExpandHeight = false;
            layout.childControlWidth = true;
            layout.childControlHeight = true;

            rows.Add(row);
            return row.transform;
        }

        private void BuildRequestRow(Relationship request)
        {
            Transform row = BuildRowShell();
            string id = request.Member.Id;

            // Not tappable through to a profile: there is no friendship yet, so there would be nothing
            // on the other side but a name you already have and two buttons already on this row.
            UIFactory.RowIdentity(row, FriendsHub.NameOf(request), "wants to be friends",
                                  ArcadeTheme.Gold, null);

            var accept = UIFactory.Button(row, "Accept", MenuButton.Variant.Primary,
                                          async () => await Run(FriendsHub.AcceptAsync(id)), 48f);
            accept.gameObject.GetComponent<LayoutElement>().preferredWidth = 130f;

            var decline = UIFactory.Button(row, "No", MenuButton.Variant.Ghost,
                                           async () => await Run(FriendsHub.DeclineAsync(id)), 48f);
            decline.gameObject.GetComponent<LayoutElement>().preferredWidth = 90f;
        }

        private void BuildFriendRow(Relationship friend)
        {
            Transform row = BuildRowShell();

            string id = friend.Member.Id;
            bool online = FriendsHub.IsOnline(friend);
            string joinCode = FriendsHub.JoinCodeFor(friend);

            string subtitle = !online ? "offline"
                : string.IsNullOrEmpty(joinCode) ? "online" : "at a table";
            Color tint = online ? ArcadeTheme.Go : ArcadeTheme.InkMuted;

            UIFactory.RowIdentity(row, FriendsHub.NameOf(friend), subtitle, tint,
                                  () => OpenProfile(id));

            // Join only. Invite lives on the friend's own screen, which the row opens when tapped.
            //
            // Both here was one button too many for the width: the row has a name, a status and an
            // avatar to fit first, and the action was being pushed off the right-hand edge. Join
            // stays because it is time-critical — they are at a table now — while an invite is a
            // message that keeps, and the extra tap buys it a screen with room to confirm on.
            if (!string.IsNullOrEmpty(joinCode))
            {
                var join = UIFactory.Button(row, "Join", MenuButton.Variant.Primary,
                                            () => Join(joinCode), 48f);
                join.gameObject.GetComponent<LayoutElement>().preferredWidth = 150f;
            }
        }

        // ---------- actions ----------

        private void OpenProfile(string friendMemberId)
        {
            Close();
            OnOpenFriendProfile?.Invoke(friendMemberId);
        }

        private async void Add()
        {
            SetStatus("SENDING…");

            if (await FriendsHub.SendRequestAsync(addField.text))
            {
                addField.text = string.Empty;
                SetStatus("REQUEST SENT");
                Rebuild();
            }
            else
            {
                SetStatus(FriendsHub.LastError);
            }
        }

        private async void AcceptInvite()
        {
            FriendsHub.Invite? invite = FriendsHub.PendingInvite;
            if (!invite.HasValue)
            {
                return;
            }

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
                Rebuild();
            }
        }

        /// <summary>Runs a hub call, reports whatever it said went wrong, and redraws either way.</summary>
        private async Task Run(Task<bool> call)
        {
            if (!await call)
            {
                SetStatus(FriendsHub.LastError);
            }

            Rebuild();
        }

        private void SetStatus(string message)
        {
            if (statusText != null)
            {
                statusText.text = message ?? string.Empty;
            }
        }

        private void Back()
        {
            Close();
            OnBack?.Invoke();
        }
    }
}
