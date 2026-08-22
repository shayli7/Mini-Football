using System;
using TableFootball.Net;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace TableFootball.UI
{
    /// <summary>
    /// The player's name, and the optional account that makes it survive this device.
    ///
    /// Three groups on one panel, in the manner of <see cref="MainMenu"/> and <see cref="GameMenu"/>:
    /// the overview, the create-account form and the sign-in form. Going back between them costs a
    /// SetActive rather than a rebuild.
    ///
    /// It reports through callbacks and owns no sequencing; <see cref="GameFlow"/> decides what comes
    /// after. Everything it knows about identity it asks <see cref="PlayerAccount"/>.
    /// </summary>
    public class ProfileMenu : MonoBehaviour
    {
        private enum Screen
        {
            Overview,
            Create,
            SignIn,
            Delete
        }

        private GameObject root;
        private CanvasGroup group;

        private GameObject overviewGroup;
        private GameObject createGroup;
        private GameObject signInGroup;
        private GameObject deleteGroup;

        private TextMeshProUGUI nameDisplay;
        private TextMeshProUGUI accountState;
        private TextMeshProUGUI statusText;
        private TextMeshProUGUI winsValue;
        private TextMeshProUGUI lossesValue;
        private TextMeshProUGUI rateValue;

        private TMP_InputField nameField;
        private TMP_InputField createUser;
        private TMP_InputField createPass;
        private TMP_InputField signInUser;
        private TMP_InputField signInPass;

        private MenuButton createButton;
        private MenuButton signInButton;

        /// <summary>Raised when the player backs out.</summary>
        public Action OnBack;

        public bool IsOpen => root != null && root.activeSelf;

        public void Build(Transform canvasRoot)
        {
            root = UIFactory.Child(canvasRoot, "ProfileMenu");
            UIFactory.Stretch(UIFactory.Rt(root));
            group = root.AddComponent<CanvasGroup>();

            UIFactory.Backdrop(root.transform);

            var title = UIFactory.Text(root.transform, "ACCOUNT", ArcadeTheme.FsTitle, ArcadeTheme.Ink,
                                       display: true, bold: true, upper: true, tracking: 8f);
            var trt = UIFactory.Rt(title.gameObject);
            trt.anchorMin = new Vector2(0f, 1f);
            trt.anchorMax = new Vector2(1f, 1f);
            trt.pivot = new Vector2(0.5f, 1f);
            trt.offsetMin = new Vector2(0f, -130f);
            trt.offsetMax = new Vector2(0f, -46f);

            var panel = UIFactory.Panel(root.transform, "ProfilePanel");
            var prt = UIFactory.Rt(panel);
            prt.anchorMin = prt.anchorMax = new Vector2(0.5f, 0.5f);
            prt.pivot = new Vector2(0.5f, 0.5f);
            // Wide and short, because that is the shape of the screen this runs on. A landscape phone
            // at 20:9 gives the canvas roughly 805 reference units of height and 1790 of width, and
            // the title and Back button take a fifth of the height between them — so a tall panel
            // runs out of room while the sides sit empty. The overview answers that by using two
            // columns; the other groups fit in one and simply leave the right side clear.
            prt.sizeDelta = new Vector2(1080f, 380f);
            prt.anchoredPosition = new Vector2(0f, -6f);

            var fill = panel.transform.Find("Fill");
            BuildOverview(fill);
            BuildCreate(fill);
            BuildSignIn(fill);
            BuildDelete(fill);

            BuildStatus(root.transform);
            BuildBack(root.transform);

            Show(Screen.Overview);
            root.SetActive(false);
        }

        private static GameObject Column(Transform parent, string name)
        {
            var column = UIFactory.Child(parent, name);
            // Tighter top and bottom than the sides: the overview is the tallest group in the game and
            // the vertical padding is the only slack it has left.
            UIFactory.Stretch(UIFactory.Rt(column), 30f, 20f, 30f, 20f);

            var layout = column.AddComponent<VerticalLayoutGroup>();
            layout.spacing = ArcadeTheme.Sm;
            layout.childAlignment = TextAnchor.UpperCenter;
            layout.childForceExpandWidth = true;
            layout.childForceExpandHeight = false;
            layout.childControlWidth = true;
            layout.childControlHeight = true;

            return column;
        }

        /// <summary>
        /// A group laid out as two columns rather than one. Used by the overview, which carries more
        /// than a landscape phone has the height to stack.
        /// </summary>
        private static GameObject Row(Transform parent, string name)
        {
            var row = UIFactory.Child(parent, name);
            UIFactory.Stretch(UIFactory.Rt(row), 30f, 20f, 30f, 20f);

            var layout = row.AddComponent<HorizontalLayoutGroup>();
            layout.spacing = ArcadeTheme.Xl2;
            layout.childAlignment = TextAnchor.UpperCenter;
            layout.childForceExpandWidth = true;
            layout.childForceExpandHeight = false;
            layout.childControlWidth = true;
            layout.childControlHeight = true;

            return row;
        }

        /// <summary>One column inside a <see cref="Row"/>. Shares the width evenly with its siblings.</summary>
        private static Transform Col(Transform row)
        {
            var col = UIFactory.Child(row, "Col");
            col.AddComponent<LayoutElement>().flexibleWidth = 1f;

            var layout = col.AddComponent<VerticalLayoutGroup>();
            layout.spacing = ArcadeTheme.Sm;
            layout.childAlignment = TextAnchor.UpperCenter;
            layout.childForceExpandWidth = true;
            layout.childForceExpandHeight = false;
            layout.childControlWidth = true;
            layout.childControlHeight = true;

            return col.transform;
        }

        private static void Caption(Transform parent, string text)
        {
            var t = UIFactory.Text(parent, text, ArcadeTheme.FsCaption, ArcadeTheme.InkMuted,
                                   display: false, bold: true, upper: true, tracking: 8f);
            t.gameObject.AddComponent<LayoutElement>().preferredHeight = 18f;
        }

        /// <summary>
        /// Two columns: who you are on the left, what you can change on the right.
        ///
        /// The split is by kind rather than by length — the left side is the read-only identity a
        /// player shows to others, the right side is every control that alters it. Balancing them by
        /// height alone would have put the delete button next to the win rate.
        /// </summary>
        private void BuildOverview(Transform parent)
        {
            overviewGroup = Row(parent, "OverviewGroup");

            var left = Col(overviewGroup.transform);
            var right = Col(overviewGroup.transform);

            Caption(left, "your friend code");

            // The one string on this screen another human has to read and type, so it gets the
            // treatment the join code gets in OnlineMenu.
            //
            // Your own name, and still not markup: it is validated on the way out now, but a name
            // set by an older build — or by anything that is not this client — arrives here anyway.
            nameDisplay = UIFactory.Text(left, "—", ArcadeTheme.FsTitle * 0.8f,
                                         ArcadeTheme.Gold, display: true, bold: true, upper: false,
                                         tracking: 4f, richText: false);
            nameDisplay.gameObject.AddComponent<LayoutElement>().preferredHeight = 46f;

            UIFactory.SectionRule(left, "online record");
            BuildRecord(left);

            accountState = UIFactory.Text(left, string.Empty, ArcadeTheme.FsCaption,
                                          ArcadeTheme.InkMuted, display: false, bold: true,
                                          upper: true, tracking: 3f);
            accountState.gameObject.AddComponent<LayoutElement>().preferredHeight = 44f;
            accountState.textWrappingMode = TextWrappingModes.Normal;

            UIFactory.SectionRule(right, "change your name");
            // Unfiltered on purpose. The rule about what a name may contain lives in
            // PlayerAccount.ValidateName, which is where it can actually be relied on — a keystroke
            // filter guards this one field and nothing else. Filtering here as well would only mean
            // silently swallowing characters the player typed instead of saying what is wrong with
            // them, and would refuse the . - _ that the rule allows.
            nameField = UIFactory.TextInput(right, "new name", 20,
                                            TMP_InputField.CharacterValidation.None,
                                            height: 50f);
            UIFactory.Button(right, "Rename", MenuButton.Variant.Neutral, Rename, 50f);

            createButton = UIFactory.Button(right, "Create Account",
                                            MenuButton.Variant.Primary, () => Show(Screen.Create), 50f);
            signInButton = UIFactory.Button(right, "Sign In",
                                            MenuButton.Variant.Ghost, () => Show(Screen.SignIn), 50f);

            // Offered to anonymous players too, not only to those with a username: the player and
            // their friends list exist either way, and erasing them is the player's call either way.
            UIFactory.Button(right, "Delete Player", MenuButton.Variant.Danger,
                             () => Show(Screen.Delete), 50f);
        }

        /// <summary>
        /// This device's online record. Read from <see cref="MatchStats"/> rather than a server: it is
        /// the player's own screen, and their own device is where the count is both authoritative and
        /// instant — a friend's record is the one that has to come from somewhere else.
        /// </summary>
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
        }

        private void BuildDelete(Transform parent)
        {
            deleteGroup = Column(parent, "DeleteGroup");

            var title = UIFactory.Text(deleteGroup.transform, "DELETE PLAYER?", ArcadeTheme.FsTitle * 0.6f,
                                       ArcadeTheme.Red, display: true, bold: true, upper: true,
                                       tracking: 4f);
            title.gameObject.AddComponent<LayoutElement>().preferredHeight = 44f;

            // Names what actually goes, rather than asking "are you sure?" about an unstated thing.
            // The friends line matters most: it is the part players do not expect to lose.
            var warn = UIFactory.Text(deleteGroup.transform,
                                      "your name, your account and your friends are erased forever. " +
                                      "friends who added you will lose you from their lists. " +
                                      "this cannot be undone.",
                                      ArcadeTheme.FsCaption, ArcadeTheme.Ink,
                                      display: false, bold: true, upper: true, tracking: 2f);
            warn.gameObject.AddComponent<LayoutElement>().preferredHeight = 96f;
            warn.textWrappingMode = TextWrappingModes.Normal;

            UIFactory.Spacer(deleteGroup.transform, 8f);

            // Keep first and primary, matching the pause menu's leave confirmation: the safe answer
            // sits under the thumb and looks like the default.
            UIFactory.Button(deleteGroup.transform, "Keep My Player", MenuButton.Variant.Primary,
                             () => Show(Screen.Overview), 54f);
            UIFactory.Button(deleteGroup.transform, "Delete Forever", MenuButton.Variant.Danger,
                             Delete, 54f);
        }

        private void BuildCreate(Transform parent)
        {
            createGroup = Column(parent, "CreateGroup");

            Caption(createGroup.transform, "choose a username");
            createUser = UIFactory.TextInput(createGroup.transform, "username", 20,
                                             TMP_InputField.CharacterValidation.None, height: 54f);

            Caption(createGroup.transform, "and a password");
            // 29, not 30: a symbol-free password has one appended before it is sent, and the service
            // caps the result at 30.
            createPass = UIFactory.TextInput(createGroup.transform, "password", 29,
                                             TMP_InputField.CharacterValidation.None,
                                             password: true, height: 54f);

            var rules = UIFactory.Text(createGroup.transform,
                                       "at least 8 characters, with a capital and a number",
                                       ArcadeTheme.FsCaption, ArcadeTheme.InkMuted,
                                       display: false, bold: true, upper: true, tracking: 2f);
            rules.gameObject.AddComponent<LayoutElement>().preferredHeight = 32f;
            rules.textWrappingMode = TextWrappingModes.Normal;

            UIFactory.Button(createGroup.transform, "Create", MenuButton.Variant.Primary, Create, 54f);
            UIFactory.Button(createGroup.transform, "Cancel", MenuButton.Variant.Ghost,
                             () => Show(Screen.Overview), 54f);
        }

        private void BuildSignIn(Transform parent)
        {
            signInGroup = Column(parent, "SignInGroup");

            Caption(signInGroup.transform, "sign in to your account");
            signInUser = UIFactory.TextInput(signInGroup.transform, "username", 20,
                                             TMP_InputField.CharacterValidation.None, height: 54f);
            signInPass = UIFactory.TextInput(signInGroup.transform, "password", 30,
                                             TMP_InputField.CharacterValidation.None,
                                             password: true, height: 54f);

            var warn = UIFactory.Text(signInGroup.transform,
                                      "this replaces the player on this device",
                                      ArcadeTheme.FsCaption, ArcadeTheme.Gold,
                                      display: false, bold: true, upper: true, tracking: 2f);
            warn.gameObject.AddComponent<LayoutElement>().preferredHeight = 32f;
            warn.textWrappingMode = TextWrappingModes.Normal;

            UIFactory.Button(signInGroup.transform, "Sign In", MenuButton.Variant.Primary, SignIn, 54f);
            UIFactory.Button(signInGroup.transform, "Cancel", MenuButton.Variant.Ghost,
                             () => Show(Screen.Overview), 54f);
        }

        private void BuildStatus(Transform parent)
        {
            statusText = UIFactory.Text(parent, string.Empty, ArcadeTheme.FsBody, ArcadeTheme.Red,
                                        display: false, bold: true, upper: true, tracking: 3f);
            var rt = UIFactory.Rt(statusText.gameObject);
            rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 0f);
            rt.pivot = new Vector2(0.5f, 0f);
            rt.sizeDelta = new Vector2(900f, 36f);
            rt.anchoredPosition = new Vector2(0f, 112f);
        }

        private void BuildBack(Transform parent)
        {
            var holder = UIFactory.Child(parent, "BackHolder");
            var rt = UIFactory.Rt(holder);
            rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 0f);
            rt.pivot = new Vector2(0.5f, 0f);
            rt.sizeDelta = new Vector2(260f, 54f);
            // Matches the friends list exactly — the two screens sit back to back, and a Back button
            // that shifted between them would read as the whole page moving.
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
            Show(Screen.Overview);

            root.SetActive(true);
            root.transform.SetAsLastSibling();
            group.blocksRaycasts = true;

            // The name may not exist yet for a brand new player — the service mints one on the first
            // request — so the panel opens showing a placeholder and fills itself in.
            await PlayerAccount.RefreshAsync();
            Redraw();
        }

        public void Close()
        {
            if (root != null)
            {
                group.blocksRaycasts = false;
                root.SetActive(false);
            }
        }

        private void Show(Screen next)
        {
            if (overviewGroup != null) overviewGroup.SetActive(next == Screen.Overview);
            if (createGroup != null) createGroup.SetActive(next == Screen.Create);
            if (signInGroup != null) signInGroup.SetActive(next == Screen.SignIn);
            if (deleteGroup != null) deleteGroup.SetActive(next == Screen.Delete);

            SetStatus(string.Empty);
        }

        private void Redraw()
        {
            if (nameDisplay == null)
            {
                return;
            }

            string name = PlayerAccount.DisplayName;
            nameDisplay.text = string.IsNullOrEmpty(name) ? "—" : name;

            winsValue.text = MatchStats.OnlineWins.ToString();
            lossesValue.text = MatchStats.OnlineLosses.ToString();
            // A dash rather than 0% before the first match: nobody has a nought-percent win rate until
            // they have actually lost one.
            rateValue.text = MatchStats.WinPercent >= 0 ? $"{MatchStats.WinPercent}%" : "–";

            if (PlayerAccount.HasAccount)
            {
                accountState.text = "your account is secured";
                accountState.color = ArcadeTheme.Go;
            }
            else
            {
                // Said plainly, because it is the one thing on this screen a player would want to know
                // before spending an evening adding friends.
                accountState.text = "this player exists on this device only — friends are lost if you reinstall";
                accountState.color = ArcadeTheme.Gold;
            }

            // Both are meaningless once an account exists: there is nothing left to create, and
            // signing in as somebody else from your own profile is not a thing anyone means to do.
            if (createButton != null) createButton.gameObject.SetActive(!PlayerAccount.HasAccount);
            if (signInButton != null) signInButton.gameObject.SetActive(!PlayerAccount.HasAccount);
        }

        private void SetStatus(string message)
        {
            if (statusText != null)
            {
                statusText.text = message ?? string.Empty;
            }
        }

        // ---------- actions ----------

        private async void Rename()
        {
            SetStatus("SAVING…");

            if (await PlayerAccount.SetNameAsync(nameField.text))
            {
                nameField.text = string.Empty;
                SetStatus(string.Empty);
                Redraw();
            }
            else
            {
                SetStatus(PlayerAccount.LastError);
            }
        }

        private async void Create()
        {
            SetStatus("CREATING…");

            if (await PlayerAccount.CreateAccountAsync(createUser.text, createPass.text))
            {
                createUser.text = string.Empty;
                createPass.text = string.Empty;
                Show(Screen.Overview);
                Redraw();
            }
            else
            {
                SetStatus(PlayerAccount.LastError);
            }
        }

        private async void SignIn()
        {
            SetStatus("SIGNING IN…");

            if (await PlayerAccount.SignInAsync(signInUser.text, signInPass.text))
            {
                signInUser.text = string.Empty;
                signInPass.text = string.Empty;
                Show(Screen.Overview);
                Redraw();
            }
            else
            {
                SetStatus(PlayerAccount.LastError);
            }
        }

        private async void Delete()
        {
            SetStatus("DELETING…");

            if (await PlayerAccount.DeleteAccountAsync())
            {
                // Straight back to the overview, which now shows a brand new anonymous player with a
                // new code and no friends. Saying so beats leaving them to notice.
                Show(Screen.Overview);
                Redraw();
                SetStatus("PLAYER DELETED — YOU ARE SOMEONE NEW");
            }
            else
            {
                SetStatus(PlayerAccount.LastError);
            }
        }

        private void Back()
        {
            Close();
            OnBack?.Invoke();
        }
    }
}
