using System;
using System.Collections;
using TableFootball.Net;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace TableFootball.UI
{
    /// <summary>
    /// The one-time welcome a brand new player meets on their first launch: sign in, create an
    /// account, or play as a guest. It sits between the title screen and the main menu, and only for
    /// somebody who has never got past it — a returning player boots straight to the menu.
    ///
    /// "New player" is a fact about this DEVICE, kept in <see cref="PlayerPrefs"/> under
    /// <see cref="DoneKey"/>. The service-side identity is a poor gate for it: an anonymous player is
    /// minted at boot before anyone has chosen anything, so "are you signed in" is true for everyone
    /// and answers a different question. What this screen actually gates on is "have you ever finished
    /// this screen", which is exactly the flag — reset by a reinstall, which is the one time a player
    /// genuinely is new to the device again and should be offered the choice afresh.
    ///
    /// Four groups stacked directly on the splash wash — no panel behind them, so the ways in read as
    /// buttons floating on the title art rather than a form in a box, the way a store's sign-in gate
    /// does. The three-button choice, the guest-name form, the sign-in form and the sign-up form each
    /// occupy the same centred column; moving between them costs a SetActive rather than a rebuild.
    ///
    /// It owns no sequencing. Every path that reaches the menu ends in <see cref="Finish"/>, which
    /// records the flag and hands control back through <see cref="onComplete"/>; <see cref="GameFlow"/>
    /// decides that "complete" means "open the menu". Everything it knows about identity it asks
    /// <see cref="PlayerAccount"/> — the same calls the account screen makes, so a guest name set here
    /// and a rename there are the one operation.
    /// </summary>
    public class OnboardingScreen : MonoBehaviour
    {
        /// <summary>Set to 1 the first time a player finishes this screen by any route. Its absence is
        /// the whole definition of "new player" — see the class summary.</summary>
        private const string DoneKey = "TableFootball.Onboarding.Completed";

        /// <summary>True for a player who has never finished onboarding on this device. Read by
        /// <see cref="GameFlow"/> to decide whether to raise this screen after the title tap.</summary>
        public static bool NeedsOnboarding => PlayerPrefs.GetInt(DoneKey, 0) == 0;

        private enum Group
        {
            Choose,
            Guest,
            Login,
            SignUp
        }

        private GameObject root;
        private CanvasGroup group;

        private GameObject chooseGroup;
        private GameObject guestGroup;
        private GameObject loginGroup;
        private GameObject signUpGroup;

        private TMP_InputField guestName;
        private TMP_InputField loginUser;
        private TMP_InputField loginPass;
        private TMP_InputField signUpUser;
        private TMP_InputField signUpPass;

        private TextMeshProUGUI statusText;

        /// <summary>Raised once the player is through — GameFlow opens the menu.</summary>
        private Action onComplete;

        public bool IsOpen => root != null && root.activeSelf;

        public void Build(Transform canvasRoot)
        {
            root = UIFactory.Child(canvasRoot, "OnboardingScreen");
            UIFactory.Stretch(UIFactory.Rt(root));
            group = root.AddComponent<CanvasGroup>();

            BuildBackdrop();
            BuildLogo();

            // A bare centred column, no panel: the groups lay their controls straight onto the wash.
            // Sized rather than stretched to the screen so the buttons keep a sensible width on a wide
            // landscape phone instead of running the full span.
            var options = UIFactory.Child(root.transform, "Options");
            var ort = UIFactory.Rt(options);
            ort.anchorMin = ort.anchorMax = new Vector2(0.5f, 0.5f);
            ort.pivot = new Vector2(0.5f, 0.5f);
            ort.sizeDelta = new Vector2(660f, 470f);
            ort.anchoredPosition = new Vector2(0f, -46f);

            BuildChoose(options.transform);
            BuildGuest(options.transform);
            BuildLogin(options.transform);
            BuildSignUp(options.transform);

            BuildStatus(root.transform);

            ShowGroup(Group.Choose);
            root.SetActive(false);
        }

        // ---------- build ----------

        /// <summary>
        /// The same coloured wash the title screen wears, so arriving here reads as the next beat of
        /// the splash rather than a different screen. Raycast-blocking: it is the floor of the front
        /// door and the table behind it must not be touchable.
        /// </summary>
        private void BuildBackdrop()
        {
            var backdrop = UIFactory.Child(root.transform, "Backdrop");
            var img = backdrop.AddComponent<Image>();
            img.sprite = ArcadeTheme.SplashBackdrop();
            img.type = Image.Type.Simple;
            img.color = Color.white;
            img.raycastTarget = true;
            UIFactory.Stretch(UIFactory.Rt(backdrop), -ArcadeTheme.Bleed);
            backdrop.AddComponent<UIAmbientDrift>();
        }

        private void BuildLogo()
        {
            var logo = UIFactory.LogoLockup(root.transform);
            var rt = UIFactory.Rt(logo);
            rt.anchorMin = new Vector2(0f, 1f);
            rt.anchorMax = new Vector2(1f, 1f);
            rt.pivot = new Vector2(0.5f, 1f);
            rt.offsetMin = new Vector2(0f, -232f);
            rt.offsetMax = new Vector2(0f, -44f);
        }

        /// <summary>A single-column group filling the options container, the shape every one of the
        /// four wears.</summary>
        private static GameObject Column(Transform parent, string name)
        {
            var column = UIFactory.Child(parent, name);
            UIFactory.Stretch(UIFactory.Rt(column), 24f, 12f, 24f, 12f);

            var layout = column.AddComponent<VerticalLayoutGroup>();
            layout.spacing = ArcadeTheme.Md;
            layout.childAlignment = TextAnchor.MiddleCenter;
            layout.childForceExpandWidth = true;
            layout.childForceExpandHeight = false;
            layout.childControlWidth = true;
            layout.childControlHeight = true;

            return column;
        }

        private static void Caption(Transform parent, string text)
        {
            var t = UIFactory.Text(parent, text, ArcadeTheme.FsCaption, ArcadeTheme.InkMuted,
                                   display: false, bold: true, upper: true, tracking: 6f);
            t.gameObject.AddComponent<LayoutElement>().preferredHeight = 20f;
        }

        private void BuildChoose(Transform parent)
        {
            chooseGroup = Column(parent, "ChooseGroup");

            var welcome = UIFactory.Text(chooseGroup.transform, "WELCOME", ArcadeTheme.FsTitle * 0.58f,
                                         ArcadeTheme.Ink, display: true, bold: true, upper: true,
                                         tracking: 6f);
            welcome.gameObject.AddComponent<LayoutElement>().preferredHeight = 40f;

            // Google Play is present but not yet wired to anything, so it is drawn disabled rather than
            // left out — the row of ways in is the whole point of this screen, and a gap where one of
            // them will go reads worse than a button that is plainly not ready.
            var google = UIFactory.Button(chooseGroup.transform, "Google Play",
                                          MenuButton.Variant.Neutral, null, 58f);
            google.interactable = false;

            UIFactory.Button(chooseGroup.transform, "Login to Account",
                             MenuButton.Variant.Ghost, () => ShowGroup(Group.Login), 58f);

            // The guest path is the primary one: it is the fastest way to the table and asks nothing
            // the player has to remember, so it gets the one resting accent this screen allows.
            UIFactory.Button(chooseGroup.transform, "Play as Guest",
                             MenuButton.Variant.Primary, () => ShowGroup(Group.Guest), 58f);

            UIFactory.Spacer(chooseGroup.transform, 2f);

            var note = UIFactory.Text(chooseGroup.transform,
                                      "guest progress is saved on this device only — sign in to keep it safe",
                                      ArcadeTheme.FsCaption * 0.9f, ArcadeTheme.InkMuted,
                                      display: false, bold: true, upper: true, tracking: 2f);
            note.gameObject.AddComponent<LayoutElement>().preferredHeight = 34f;
            note.textWrappingMode = TextWrappingModes.Normal;
        }

        private void BuildGuest(Transform parent)
        {
            guestGroup = Column(parent, "GuestGroup");

            Caption(guestGroup.transform, "pick a name others will see");
            // Unfiltered, exactly like the rename field: the rule about what a name may contain lives
            // in PlayerAccount.ValidateName, the one place it can be relied on. Filtering keystrokes
            // here would only swallow characters silently and still refuse the . - _ the rule allows.
            guestName = UIFactory.TextInput(guestGroup.transform, "your name", 20,
                                            TMP_InputField.CharacterValidation.None, height: 56f);

            UIFactory.Spacer(guestGroup.transform, 4f);

            UIFactory.Button(guestGroup.transform, "Play", MenuButton.Variant.Primary, PlayAsGuest, 56f);
            UIFactory.Button(guestGroup.transform, "Back", MenuButton.Variant.Ghost,
                             () => ShowGroup(Group.Choose), 56f);
        }

        private void BuildLogin(Transform parent)
        {
            loginGroup = Column(parent, "LoginGroup");

            Caption(loginGroup.transform, "sign in to your account");
            // No randomiser here: the username on the sign-in form is one you already have and must
            // type exactly. It only earns a dice on the sign-up form, where the name is being invented.
            loginUser = UIFactory.TextInput(loginGroup.transform, "username", 20,
                                            TMP_InputField.CharacterValidation.None, height: 54f);
            loginPass = UIFactory.PasswordField(loginGroup.transform, "password", 30, 54f);

            UIFactory.Button(loginGroup.transform, "Sign In", MenuButton.Variant.Primary, SignIn, 54f);

            // The way to an account for a player who does not have one yet. A Ghost button rather than
            // a bare line of text, so it reads as the second action it is.
            UIFactory.Button(loginGroup.transform, "Create New Account", MenuButton.Variant.Ghost,
                             () => ShowGroup(Group.SignUp), 54f);
            UIFactory.Button(loginGroup.transform, "Back", MenuButton.Variant.Ghost,
                             () => ShowGroup(Group.Choose), 54f);
        }

        private void BuildSignUp(Transform parent)
        {
            signUpGroup = Column(parent, "SignUpGroup");

            Caption(signUpGroup.transform, "create an account");
            // The dice fills a valid random username in one tap, for a player who does not want to
            // think one up. It writes straight into the field, so it is still theirs to edit or clear.
            signUpUser = UIFactory.UsernameField(signUpGroup.transform, "username", 20, 52f);
            // 29, not 30: a symbol-free password has one appended before it is sent, and the service
            // caps the result at 30. The same limit the account screen's create form uses.
            signUpPass = UIFactory.PasswordField(signUpGroup.transform, "password", 29, 52f);

            var rules = UIFactory.Text(signUpGroup.transform,
                                       "at least 8 characters, with a capital and a number",
                                       ArcadeTheme.FsCaption * 0.9f, ArcadeTheme.InkMuted,
                                       display: false, bold: true, upper: true, tracking: 2f);
            rules.gameObject.AddComponent<LayoutElement>().preferredHeight = 30f;
            rules.textWrappingMode = TextWrappingModes.Normal;

            UIFactory.Button(signUpGroup.transform, "Create Account", MenuButton.Variant.Primary,
                             CreateAccount, 52f);
            UIFactory.Button(signUpGroup.transform, "Back", MenuButton.Variant.Ghost,
                             () => ShowGroup(Group.Login), 52f);
        }

        private void BuildStatus(Transform parent)
        {
            statusText = UIFactory.Text(parent, string.Empty, ArcadeTheme.FsBody, ArcadeTheme.Red,
                                        display: false, bold: true, upper: true, tracking: 3f);
            var rt = UIFactory.Rt(statusText.gameObject);
            rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 0f);
            rt.pivot = new Vector2(0.5f, 0f);
            rt.sizeDelta = new Vector2(900f, 36f);
            rt.anchoredPosition = new Vector2(0f, 70f);
        }

        // ---------- show / state ----------

        /// <summary>
        /// Raises the screen and remembers where to hand control when the player is through. Mirrors
        /// <see cref="MainMenu.Open"/>'s handover: SetActive, take the front, fade up — so the title
        /// screen fading out over the top of this crossfades into it rather than cutting.
        /// </summary>
        public void Show(Action onComplete)
        {
            if (root == null)
            {
                onComplete?.Invoke();
                return;
            }

            this.onComplete = onComplete;

            SetStatus(string.Empty);
            ShowGroup(Group.Choose);

            root.SetActive(true);
            root.transform.SetAsLastSibling();
            group.blocksRaycasts = true;

            group.alpha = 0f;
            StartCoroutine(UITween.Fade(group, 0f, 1f, ArcadeTheme.TNormal, ArcadeTheme.EaseOut));
        }

        private void Close()
        {
            if (root != null)
            {
                group.blocksRaycasts = false;
                root.SetActive(false);
            }
        }

        private void ShowGroup(Group next)
        {
            if (chooseGroup != null) chooseGroup.SetActive(next == Group.Choose);
            if (guestGroup != null) guestGroup.SetActive(next == Group.Guest);
            if (loginGroup != null) loginGroup.SetActive(next == Group.Login);
            if (signUpGroup != null) signUpGroup.SetActive(next == Group.SignUp);

            SetStatus(string.Empty);
        }

        private void SetStatus(string message)
        {
            if (statusText != null)
            {
                statusText.text = message ?? string.Empty;
            }
        }

        // ---------- actions ----------

        private async void PlayAsGuest()
        {
            SetStatus("SAVING…");

            if (await PlayerAccount.SetNameAsync(guestName.text))
            {
                Finish();
            }
            else
            {
                SetStatus(PlayerAccount.LastError);
            }
        }

        private async void SignIn()
        {
            SetStatus("SIGNING IN…");

            if (await PlayerAccount.SignInAsync(loginUser.text, loginPass.text))
            {
                Finish();
            }
            else
            {
                SetStatus(PlayerAccount.LastError);
            }
        }

        private async void CreateAccount()
        {
            SetStatus("CREATING…");

            if (await PlayerAccount.CreateAccountAsync(signUpUser.text, signUpPass.text))
            {
                Finish();
            }
            else
            {
                SetStatus(PlayerAccount.LastError);
            }
        }

        /// <summary>
        /// The one exit every successful path shares: record that this device's player is no longer new
        /// so the screen never shows again, then hand control back to GameFlow to open the menu.
        /// </summary>
        private void Finish()
        {
            PlayerPrefs.SetInt(DoneKey, 1);
            PlayerPrefs.Save();

            Close();
            onComplete?.Invoke();
        }
    }
}
