using System;
using TableFootball.Net;
using TableFootball.Progression;
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

        private TextMeshProUGUI accountState;
        private TextMeshProUGUI statusText;
        private TextMeshProUGUI winsValue;
        private TextMeshProUGUI lossesValue;
        private TextMeshProUGUI rateValue;
        private TextMeshProUGUI playedValue;
        private TextMeshProUGUI goalsValue;
        private TextMeshProUGUI timeValue;
        private TextMeshProUGUI levelValue;

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

            // The shared translucent dim, not an opaque backdrop: the account screen now wears the same
            // background as the settings/pause overlay, so the two read as one design language over the
            // same lit table rather than two different screens.
            UIFactory.ScrimDim(root.transform);

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
            prt.sizeDelta = new Vector2(1080f, 430f);
            prt.anchoredPosition = new Vector2(0f, -18f);

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

            // The identity header: avatar, name, level and XP — the same chip the main menu leads
            // with, so "you" looks the same everywhere. The name it shows is this screen's friend
            // code; the level and XP are visual placeholders (PlayerProgress).
            var chipHolder = UIFactory.Child(left, "ChipHolder");
            chipHolder.AddComponent<LayoutElement>().preferredHeight = 66f;
            var chipGo = UIFactory.Child(chipHolder.transform, "Chip");
            chipGo.AddComponent<ProfileChip>().Build(chipHolder.transform);

            var codeHint = UIFactory.Text(left, "this name is your friend code — share it to be added",
                                          ArcadeTheme.FsCaption * 0.86f, ArcadeTheme.InkMuted,
                                          display: false, bold: true, upper: true, tracking: 3f);
            codeHint.gameObject.AddComponent<LayoutElement>().preferredHeight = 18f;

            UIFactory.SectionRule(left, "record");
            BuildRecord(left);
            BuildRecord2(left);

            accountState = UIFactory.Text(left, string.Empty, ArcadeTheme.FsCaption,
                                          ArcadeTheme.InkMuted, display: false, bold: true,
                                          upper: true, tracking: 3f);
            accountState.gameObject.AddComponent<LayoutElement>().preferredHeight = 40f;
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
            UIFactory.Button(right, "Rename", MenuButton.Variant.Blue, Rename, 50f);

            createButton = UIFactory.Button(right, "Create Account",
                                            MenuButton.Variant.Primary, () => Show(Screen.Create), 50f);
            signInButton = UIFactory.Button(right, "Sign In",
                                            MenuButton.Variant.Blue, () => Show(Screen.SignIn), 50f);

            // Offered to anonymous players too, not only to those with a username: the player and
            // their friends list exist either way, and erasing them is the player's call either way.
            UIFactory.Button(right, "Delete Player", MenuButton.Variant.Danger,
                             () => Show(Screen.Delete), 50f);
        }

        /// <summary>
        /// This device's online record. Read from <see cref="MatchStats"/> rather than a server: it is
        /// the player's own screen, and their own device is where the count is both authoritative and
        /// instant — a friend's record is the one that has to come from somewhere else.
        ///
        /// The level sits in the same row rather than getting a block of its own. The overview is
        /// already the tallest group in the game — the comment on <see cref="Column"/> says as much —
        /// and a fourth number costs no height at all, where a ring would have cost 140. The big ring
        /// lives on the quests screen, which has room for it.
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

            // One colour for the record, with gold kept for the win rate — the figure the rest add
            // up to. Seven stats in five colours read as a chart legend rather than a record.
            winsValue = UIFactory.StatBlock(row.transform, "won", ArcadeTheme.Ink);
            lossesValue = UIFactory.StatBlock(row.transform, "lost", ArcadeTheme.Ink);
            rateValue = UIFactory.StatBlock(row.transform, "win rate", ArcadeTheme.Gold);
            levelValue = UIFactory.StatBlock(row.transform, "level", ArcadeTheme.Ink);
        }

        /// <summary>
        /// A second stat row, all real now: matches played, goals scored and total time played — the
        /// numbers a returning player checks to feel their history adding up. Goals and time are
        /// credited by <see cref="TableFootball.UI.GameFlow"/> off the match loop; played is wins plus
        /// losses.
        /// </summary>
        private void BuildRecord2(Transform parent)
        {
            var row = UIFactory.Child(parent, "RecordRow2");
            row.AddComponent<LayoutElement>().preferredHeight = 76f;

            var h = row.AddComponent<HorizontalLayoutGroup>();
            h.childAlignment = TextAnchor.MiddleCenter;
            h.spacing = ArcadeTheme.Sm;
            h.childForceExpandWidth = true;
            h.childForceExpandHeight = false;
            h.childControlWidth = true;
            h.childControlHeight = true;

            playedValue = UIFactory.StatBlock(row.transform, "played", ArcadeTheme.Ink);
            goalsValue = UIFactory.StatBlock(row.transform, "goals", ArcadeTheme.Ink);
            timeValue = UIFactory.StatBlock(row.transform, "time played", ArcadeTheme.Ink);
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
            // Dice fills a valid random username for a player who does not want to invent one.
            createUser = UIFactory.UsernameField(createGroup.transform, "username", 20, 54f);

            Caption(createGroup.transform, "and a password");
            // 29, not 30: a symbol-free password has one appended before it is sent, and the service
            // caps the result at 30. Eye toggles it between masked and plain.
            createPass = UIFactory.PasswordField(createGroup.transform, "password", 29, 54f);

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
            // No randomiser: the sign-in username is one you already have and must type exactly. The
            // eye still helps here — a mistyped password is the usual reason a sign-in fails.
            signInUser = UIFactory.TextInput(signInGroup.transform, "username", 20,
                                             TMP_InputField.CharacterValidation.None, height: 54f);
            signInPass = UIFactory.PasswordField(signInGroup.transform, "password", 30, 54f);

            var warn = UIFactory.Text(signInGroup.transform,
                                      "this replaces the player on this device",
                                      ArcadeTheme.FsCaption, ArcadeTheme.Gold,
                                      display: false, bold: true, upper: true, tracking: 2f);
            warn.gameObject.AddComponent<LayoutElement>().preferredHeight = 32f;
            warn.textWrappingMode = TextWrappingModes.Normal;

            UIFactory.Button(signInGroup.transform, "Sign In", MenuButton.Variant.Primary, SignIn, 54f);
            // A way straight to the create form for a player who came here to sign in and realised they
            // have no account yet — otherwise they would have to back out to the overview to find it.
            UIFactory.Button(signInGroup.transform, "Sign Up", MenuButton.Variant.Ghost,
                             () => Show(Screen.Create), 54f);
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

            // A quick fade in, so arriving here reads as a transition rather than a hard cut.
            group.alpha = 0f;
            StartCoroutine(UITween.Fade(group, 0f, 1f, ArcadeTheme.TFast, ArcadeTheme.EaseOut));

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
            if (accountState == null)
            {
                return;
            }

            // The name is drawn by the ProfileChip header, which follows PlayerAccount on its own —
            // nothing to set here.

            // The overall progression record (vs-AI and online alike) — the same store the XP bar and
            // level read from, so the profile is internally consistent. MatchStats stays the separate
            // online-only feed for the leaderboard.
            winsValue.text = PlayerProgress.Wins.ToString();
            lossesValue.text = PlayerProgress.Losses.ToString();
            // A dash rather than 0% before the first match: nobody has a nought-percent win rate until
            // they have actually lost one.
            // Reads PlayerProgress like winsValue/lossesValue just above, not MatchStats — the
            // percentage has to be computed from the same pair of numbers the player sees it beside,
            // or a rate and a scoreline that came from two different counters would visibly disagree.
            rateValue.text = PlayerProgress.WinPercent >= 0 ? $"{PlayerProgress.WinPercent}%" : "–";
            levelValue.text = PlayerXp.Level.ToString();

            // All real now: played is wins + losses, goals and time are credited from the match loop.
            playedValue.text = PlayerProgress.Played.ToString();
            goalsValue.text = PlayerProgress.Goals.ToString();
            timeValue.text = FormatPlayTime(PlayerProgress.PlayTimeSeconds);

            // Only the warning earns a line. "Your account is secured" restated something the player
            // had just done and could do nothing about — the anonymous case is the one with a
            // consequence worth stating before they spend an evening adding friends.
            if (PlayerAccount.HasAccount)
            {
                accountState.gameObject.SetActive(false);
            }
            else
            {
                accountState.gameObject.SetActive(true);
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

        /// <summary>
        /// Seconds as a compact "2h 14m" / "14m" / "0m", for the stat block. Hours and minutes only —
        /// a lifetime total measured to the second would be noise on a card whose job is to feel like a
        /// growing number.
        /// </summary>
        private static string FormatPlayTime(int seconds)
        {
            int minutes = seconds / 60;
            int hours = minutes / 60;
            minutes %= 60;
            return hours > 0 ? $"{hours}h {minutes}m" : $"{minutes}m";
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
