using System;
using System.Collections.Generic;
using TableFootball.Net;
using TableFootball.UI.Toolkit;
using UnityEngine;
using UnityEngine.UIElements;

namespace TableFootball.UI
{
    /// <summary>
    /// Settings: one screen that scrolls down through three sections — Account, Game Settings, About —
    /// with a rail on the left that jumps between them. Built on UI Toolkit (see
    /// <see cref="UiToolkitHost"/>) and styled by <c>Resources/UI/Styles/Settings.uss</c>.
    ///
    /// Opened from the gear on the profile and nowhere else; the pause menu no longer has settings.
    /// Like every screen it reports through callbacks and starts nothing itself — <see cref="GameFlow"/>
    /// decides where Back goes and what follows a log out.
    ///
    /// Everything that can lose the player something (changing a password, logging out, deleting the
    /// account) happens in a sheet over the list, so it is always a deliberate second step. Deleting
    /// takes a hold, not a tap. What each control does to the account is <see cref="PlayerAccount"/>'s
    /// business; this class only asks it and reports what it said.
    /// </summary>
    public class SettingsScreen : MonoBehaviour
    {
        private const string ContactUrl = "mailto:bluejayproductionsupport@gmail.com";
        private const string TermsUrl = "https://sites.google.com/view/mini-football-terms-of-service/%D7%91%D7%99%D7%AA";
        private const string PrivacyUrl = "https://sites.google.com/view/mini-football-privacy-policy/%D7%91%D7%99%D7%AA";
        private const string DeleteDataUrl = "https://sites.google.com/view/mini-football-delete-profile/%D7%91%D7%99%D7%AA";

        /// <summary>How long the delete button has to be held, in seconds.</summary>
        private const float HoldSeconds = 1.5f;

        private static readonly string[] SensitivityNames = { "LOWEST", "LOW", "MID", "HIGH", "MAX" };
        private static readonly string[] RuleNames = { "8+", "A-Z", "a-z", "0-9", "MATCH" };

        private VisualElement root;
        private ScrollView scroll;
        private VisualElement accountBox;
        private readonly VisualElement[] heads = new VisualElement[3];
        private readonly List<VisualElement> tabs = new List<VisualElement>();
        private IVisualElementScheduledItem scrollAnim;

        private UiCells musicCells, sfxCells, touchCells;
        private Label musicReadout, sfxReadout, touchReadout;
        private UiSwitch vibrationSwitch, motionSwitch;

        private VisualElement sheetLayer;
        private Label sheetStatus;
        private UiFrame toast;
        private Label toastLabel;
        private IVisualElementScheduledItem toastItem;

        // Password sheet
        private VisualElement passwordBody;
        private VisualElement stepTwo;
        private int passwordStep;
        private string currentPassword = string.Empty, newPassword = string.Empty, confirmPassword = string.Empty;
        private bool showPasswords;
        private readonly List<TextField> secretFields = new List<TextField>();
        private readonly List<UiIcon> eyeIcons = new List<UiIcon>();

        // Hold to delete
        private UiFrame holdButton;
        private VisualElement holdFill;
        private IVisualElementScheduledItem holdItem;
        private float holdStartedAt;
        private int holdQuarter;

        private bool busy;

        /// <summary>Raised when the player backs out.</summary>
        public Action OnBack;

        /// <summary>Raised after a successful log out. The player is a fresh guest by then.</summary>
        public Action OnLoggedOut;

        public bool IsOpen => root != null && root.style.display != DisplayStyle.None;

        // ---------- build ----------

        public void Build()
        {
            root = UiKit.El("screen settings", UiToolkitHost.Root, "Settings");
            var sheet = Resources.Load<StyleSheet>("UI/Styles/Settings");
            if (sheet != null) root.styleSheets.Add(sheet);
            // The floor of the screen: nothing behind it may be touched while it is up.
            root.pickingMode = PickingMode.Position;

            UiBackdrop.AddTo(root);

            BuildHeader();
            BuildRail();
            BuildList();
            BuildSheetLayer();
            BuildToast();

            UiFonts.Apply(root);
            UiKit.Show(root, false);
        }

        private void BuildHeader()
        {
            var header = UiKit.El("settings__header", root);

            var back = UiKit.Frame("sq settings__back", header);
            back.Add(new UiIcon(UiIcon.Glyph.ChevronLeft, ArcadeTheme.Ink, 2.5f));
            UiKit.OnTap(back, () => OnBack?.Invoke(), UiSound.Back);

            var box = UiKit.El("settings__title-box", header);
            var row = UiKit.El("settings__title-row", box);
            row.Add(new UiIcon(UiIcon.Glyph.Gear, ArcadeTheme.Pulse, 2f));
            UiKit.Text("SETTINGS", "settings__title f-display", row);
            UiKit.El("settings__bar", box);
        }

        private void BuildRail()
        {
            var rail = UiKit.El("settings__rail", root);
            AddTab(rail, 0, UiIcon.Glyph.Person, "ACCOUNT");
            AddTab(rail, 1, UiIcon.Glyph.Sliders, "GAME");
            AddTab(rail, 2, UiIcon.Glyph.Info, "ABOUT");
            SetActiveTab(0);
        }

        private void AddTab(VisualElement rail, int index, UiIcon.Glyph glyph, string label)
        {
            var tab = UiKit.Frame("tab", rail);
            tab.Add(new UiIcon(glyph, ArcadeTheme.Pulse, 2f));
            UiKit.Text(label, "tab__label f-display", tab);
            UiKit.Text("0" + (index + 1), "tab__num f-display-semi", tab);
            UiKit.OnTap(tab, () => ScrollToSection(index));
            tabs.Add(tab);
        }

        private void SetActiveTab(int index)
        {
            for (int i = 0; i < tabs.Count; i++) tabs[i].EnableInClassList("is-active", i == index);
        }

        private void BuildList()
        {
            scroll = new ScrollView(ScrollViewMode.Vertical) { name = "SettingsScroll" };
            scroll.AddToClassList("settings__scroll");
            scroll.horizontalScrollerVisibility = ScrollerVisibility.Hidden;
            scroll.verticalScrollerVisibility = ScrollerVisibility.Hidden;
            scroll.touchScrollBehavior = ScrollView.TouchScrollBehavior.Elastic;
            root.Add(scroll);

            var list = scroll.contentContainer;
            list.AddToClassList("settings__list");

            heads[0] = SectionHead(list, "01", "ACCOUNT", true);
            accountBox = UiKit.El(string.Empty, list);
            BuildAccount();

            heads[1] = SectionHead(list, "02", "GAME SETTINGS", false);
            BuildGame(list);

            heads[2] = SectionHead(list, "03", "ABOUT", false);
            BuildAbout(list);

#if UNITY_EDITOR || DEVELOPMENT_BUILD
            // Not in `heads`, so the rail never gains a tab for it: it is a tool, not a setting.
            SectionHead(list, "DEV", "DEVELOPER", false);
            BuildDeveloper(list);
#endif

            scroll.verticalScroller.valueChanged += OnScrolled;
        }

        private static VisualElement SectionHead(VisualElement parent, string number, string title, bool first)
        {
            var head = UiKit.El("sec-head" + (first ? " sec-head--first" : string.Empty), parent);
            UiKit.Text(number, "sec-head__num f-display-semi", head);
            UiKit.Text(title, "sec-head__title f-display", head);
            UiKit.El("sec-head__rule", head);
            return head;
        }

        // ---------- rows ----------

        private static UiFrame Row(VisualElement parent, UiIcon.Glyph glyph, string label,
                                   string extraClasses = null, Color? iconColor = null)
        {
            var row = UiKit.Frame("srow" + (extraClasses != null ? " " + extraClasses : string.Empty), parent);
            var tile = UiKit.Frame("srow__tile", row);
            tile.Add(new UiIcon(glyph, iconColor ?? ArcadeTheme.Pulse, 2f));
            UiKit.Text(label, "srow__label f-display-semi", row);
            return row;
        }

        /// <summary>A row you tap, with a small arrow (or another glyph) on the right.</summary>
        private static UiFrame ActionRow(VisualElement parent, UiIcon.Glyph glyph, string label, Action onTap,
                                         UiSound sound = UiSound.Open, UiIcon.Glyph trailing = UiIcon.Glyph.ChevronRight,
                                         string extraClasses = null, Color? iconColor = null)
        {
            var row = Row(parent, glyph, label, "srow--tap" + (extraClasses != null ? " " + extraClasses : string.Empty),
                          iconColor);
            var arrow = new UiIcon(trailing, ArcadeTheme.InkMuted, 2.4f);
            arrow.AddToClassList("srow__chevron");
            row.Add(arrow);
            UiKit.OnTap(row, onTap, sound);
            return row;
        }

        private static UiCells CellsRow(VisualElement parent, UiIcon.Glyph glyph, string label, int count, int min,
                                        out Label readout)
        {
            var row = Row(parent, glyph, label);
            var cells = new UiCells(count, min);
            row.Add(cells);
            readout = UiKit.Text(string.Empty, "srow__readout f-display", row);
            return cells;
        }

        private static UiSwitch SwitchRow(VisualElement parent, UiIcon.Glyph glyph, string label, Action<bool> set)
        {
            var row = Row(parent, glyph, label, "srow--tap");
            var toggle = new UiSwitch();
            row.Add(toggle);
            UiKit.OnTap(row, () =>
            {
                toggle.On = !toggle.On;
                set(toggle.On);
            }, UiSound.Toggle);
            return toggle;
        }

        // ---------- account ----------

        private void RebuildAccount()
        {
            if (IsOpen) BuildAccount();
        }

        private void BuildAccount()
        {
            accountBox.Clear();
            bool signedIn = PlayerAccount.HasAccount;

            // Who you are.
            var identity = UiKit.El("identity", accountBox);
            string name = PlayerAccount.ShortName;
            var hex = new UiHex { Edge = signedIn ? ArcadeTheme.Pulse : ArcadeTheme.Reward };
            hex.AddToClassList("identity__hex");
            UiKit.Text(string.IsNullOrWhiteSpace(name) ? "?" : name.Substring(0, 1).ToUpperInvariant(),
                       "identity__initial f-display", hex).style.color =
                signedIn ? ArcadeTheme.Pulse : ArcadeTheme.Reward;
            identity.Add(hex);

            var words = UiKit.El("identity__words", identity);
            UiKit.Text(string.IsNullOrWhiteSpace(name) ? "—" : name, "identity__name f-display", words);
            var state = UiKit.El("identity__state", words);
            var chip = UiKit.El("chip-state" + (signedIn ? string.Empty : " chip-state--guest"), state);
            if (signedIn) chip.Add(new UiIcon(UiIcon.Glyph.Check, ArcadeTheme.Pulse, 3.5f));
            UiKit.Text(signedIn ? "SIGNED IN" : "GUEST", "chip-state__label f-display", chip);
            UiKit.Text(signedIn ? "Progress saved to the cloud" : "Saved on this phone only",
                       "identity__note f-body-semi", state);

            if (!signedIn)
            {
                // The one row on the screen that glows: a guest can lose everything with the phone.
                var feature = UiKit.Frame("srow srow--feature srow--tap", accountBox);
                var tile = UiKit.Frame("srow__tile", feature);
                tile.Add(new UiIcon(UiIcon.Glyph.AddPerson, ArcadeTheme.OnPulse, 2.2f));
                var text = UiKit.El("srow__words", feature);
                UiKit.Text("SIGN UP", "srow__label f-display", text);
                UiKit.Text("Keep your progress", "srow__sub f-body-semi", text);
                var arrow = new UiIcon(UiIcon.Glyph.ChevronRight, ArcadeTheme.Pulse, 2.5f);
                arrow.AddToClassList("srow__chevron");
                feature.Add(arrow);
                UiKit.OnTap(feature, OpenSignUpSheet, UiSound.Open);
            }
            else
            {
                ActionRow(accountBox, UiIcon.Glyph.Key, "CHANGE PASSWORD", OpenPasswordSheet);
                ActionRow(accountBox, UiIcon.Glyph.LogOut, "LOG OUT", OpenLogOutSheet);
            }

            // Last, and red.
            ActionRow(accountBox, UiIcon.Glyph.Trash, "DELETE ACCOUNT", OpenDeleteSheet, UiSound.Warn,
                      UiIcon.Glyph.ChevronRight, "srow--danger", ArcadeTheme.Danger);

            UiFonts.Apply(accountBox);
        }

        // ---------- game settings ----------

        private void BuildGame(VisualElement list)
        {
            musicCells = CellsRow(list, UiIcon.Glyph.Music, "MUSIC", 10, 0, out musicReadout);
            musicCells.OnChanged = v =>
            {
                GameAudio.Music = v / 10f;
                musicReadout.text = Percent(v);
                GameSfx.PlayUi(UiSound.Step, 0.9f + 0.03f * v);
            };

            sfxCells = CellsRow(list, UiIcon.Glyph.Speaker, "SOUND FX", 10, 0, out sfxReadout);
            sfxCells.OnChanged = v =>
            {
                GameAudio.Sfx = v / 10f;
                sfxReadout.text = Percent(v);
                GameSfx.PlayUi(UiSound.Step, 0.9f + 0.03f * v);
            };

            vibrationSwitch = SwitchRow(list, UiIcon.Glyph.Vibrate, "VIBRATION", on =>
            {
                GameAudio.Vibration = on;
                if (on) Haptics.Play(0.7f); // a sample, so the switch proves itself
            });

            touchCells = CellsRow(list, UiIcon.Glyph.Touch, "TOUCH SENSITIVITY", GameAudio.TouchSensitivitySteps, 1,
                                  out touchReadout);
            touchReadout.AddToClassList("srow__readout--word");
            touchCells.OnChanged = v =>
            {
                GameAudio.TouchSensitivity = v;
                touchReadout.text = SensitivityNames[v - 1];
                GameSfx.PlayUi(UiSound.Step, 0.9f + 0.05f * v);
            };

            motionSwitch = SwitchRow(list, UiIcon.Glyph.Motion, "REDUCE MOTION", on =>
            {
                ArcadeTheme.ReducedMotion = on;
                UiToolkitHost.FullRoot.EnableInClassList("reduced-motion", on);
            });
        }

        /// <summary>Puts every game-settings control back on what is saved — the values can change
        /// elsewhere between visits.</summary>
        private void RefreshGame()
        {
            int music = Mathf.RoundToInt(GameAudio.Music * 10f);
            musicCells.Value = music;
            musicReadout.text = Percent(music);

            int sfx = Mathf.RoundToInt(GameAudio.Sfx * 10f);
            sfxCells.Value = sfx;
            sfxReadout.text = Percent(sfx);

            vibrationSwitch.On = GameAudio.Vibration;
            touchCells.Value = GameAudio.TouchSensitivity;
            touchReadout.text = SensitivityNames[GameAudio.TouchSensitivity - 1];
            motionSwitch.On = ArcadeTheme.ReducedMotion;
        }

        private static string Percent(int tenths) => tenths * 10 + "%";

        // ---------- about ----------

        private static void BuildAbout(VisualElement list)
        {
            ActionRow(list, UiIcon.Glyph.Mail, "CONTACT US", () => Application.OpenURL(ContactUrl),
                      UiSound.Tap, UiIcon.Glyph.LinkOut);
            ActionRow(list, UiIcon.Glyph.Doc, "TERMS OF SERVICE", () => Application.OpenURL(TermsUrl),
                      UiSound.Tap, UiIcon.Glyph.LinkOut);
            ActionRow(list, UiIcon.Glyph.Shield, "PRIVACY POLICY", () => Application.OpenURL(PrivacyUrl),
                      UiSound.Tap, UiIcon.Glyph.LinkOut);
            ActionRow(list, UiIcon.Glyph.Trash, "DELETE MY DATA", () => Application.OpenURL(DeleteDataUrl),
                      UiSound.Tap, UiIcon.Glyph.LinkOut);

            var version = Row(list, UiIcon.Glyph.Info, "VERSION", "srow--info", ArcadeTheme.InkMuted);
            UiKit.Text("v" + Application.version, "srow__value f-display", version);
        }

#if UNITY_EDITOR || DEVELOPMENT_BUILD
        // ---------- developer (Editor and Development Builds only) ----------

        /// <summary>
        /// Unlock or clear the whole collection, for checking skins and the store. Compiled out of
        /// release builds entirely. Either action pauses progression sync (a local sandbox) so the
        /// real account is never touched — see <see cref="DevCollection"/> — and the third row is the
        /// way back to the account's own collection.
        /// </summary>
        private void BuildDeveloper(VisualElement list)
        {
            ActionRow(list, UiIcon.Glyph.Chest, "UNLOCK ALL SKINS", () =>
            {
                int n = DevCollection.GrantAll();
                Toast($"DEV: {n} UNLOCKED · SYNC PAUSED");
            }, UiSound.Tap, UiIcon.Glyph.Check);

            ActionRow(list, UiIcon.Glyph.Trash, "REMOVE ALL SKINS", () =>
            {
                DevCollection.RemoveAll();
                Toast("DEV: COLLECTION CLEARED · SYNC PAUSED");
            }, UiSound.Warn, UiIcon.Glyph.Close);

            ActionRow(list, UiIcon.Glyph.Globe, "RESTORE MY REAL COLLECTION", async () =>
            {
                bool ok = await DevCollection.RestoreRealAsync();
                Toast(ok ? "DEV: REAL COLLECTION RESTORED" : "DEV: OFFLINE · RESTORES ON NEXT SIGN-IN");
            }, UiSound.Tap, UiIcon.Glyph.ChevronRight);
        }
#endif

        // ---------- scrolling ----------

        private void OnScrolled(float value)
        {
            int active = 0;
            for (int i = 0; i < heads.Length; i++)
            {
                if (heads[i].layout.y - 60f <= value) active = i;
            }

            // The last section is often too short to ever reach the top; the bottom of the list counts.
            if (value >= scroll.verticalScroller.highValue - 4f) active = heads.Length - 1;
            SetActiveTab(active);
        }

        private void ScrollToSection(int index)
        {
            SetActiveTab(index);
            float target = Mathf.Max(0f, heads[index].layout.y - 4f);

            scrollAnim?.Pause();
            if (ArcadeTheme.ReducedMotion)
            {
                scroll.scrollOffset = new Vector2(0f, target);
                return;
            }

            float from = scroll.scrollOffset.y;
            float start = Time.unscaledTime;
            scrollAnim = scroll.schedule.Execute(() =>
            {
                float t = Mathf.Clamp01((Time.unscaledTime - start) / 0.22f);
                float k = 1f - (1f - t) * (1f - t) * (1f - t);
                scroll.scrollOffset = new Vector2(0f, Mathf.Lerp(from, target, k));
                if (t >= 1f) scrollAnim.Pause();
            }).Every(16);
        }

        // ---------- open / close ----------

        public void Open()
        {
            if (root == null) return;

            PlayerAccount.OnChanged -= RebuildAccount;
            PlayerAccount.OnChanged += RebuildAccount;

            BuildAccount();
            RefreshGame();
            HideSheet();

            UiKit.Show(root, true);
            root.BringToFront();
            scroll.scrollOffset = Vector2.zero;
            SetActiveTab(0);
            root.style.opacity = 0f;
            UiKit.Fade(root, 1f, ArcadeTheme.TFast);
            UiKit.Enter(scroll, 0.08f);

            GameSfx.PlayUi(UiSound.Open);
        }

        public void Close()
        {
            PlayerAccount.OnChanged -= RebuildAccount;
            CancelHold();
            if (root == null) return;
            HideSheet();
            UiKit.Show(root, false);
        }

        private void OnDestroy() => PlayerAccount.OnChanged -= RebuildAccount;

        // ---------- sheets ----------

        private void BuildSheetLayer()
        {
            sheetLayer = UiKit.El("bleed sheet-layer", root);
            sheetLayer.pickingMode = PickingMode.Position;
            UiKit.Show(sheetLayer, false);
        }

        private void ShowSheet(UiFrame sheet, UiSound sound = UiSound.Open)
        {
            sheetLayer.Clear();
            sheetLayer.Add(sheet);
            UiFonts.Apply(sheet);
            UiKit.Show(sheetLayer, true);
            sheetLayer.style.opacity = 0f;
            UiKit.Fade(sheetLayer, 1f, ArcadeTheme.TFast);
            GameSfx.PlayUi(sound);
        }

        private void HideSheet()
        {
            CancelHold();
            busy = false;
            if (sheetLayer == null) return;
            UiKit.Show(sheetLayer, false);
            sheetLayer.Clear();
        }

        /// <summary>Closes the sheet and plays the close sound, unless the tap that closed it already made one.</summary>
        private void CloseSheet(bool sound = true)
        {
            HideSheet();
            if (sound) GameSfx.PlayUi(UiSound.Close);
        }

        private static UiFrame NewSheet(int width)
        {
            var sheet = UiKit.Frame("sheet");
            sheet.style.width = width;
            return sheet;
        }

        /// <summary>The top row of a sheet: an icon tile, the title and a close button.</summary>
        private void SheetHead(VisualElement sheet, UiIcon.Glyph glyph, string title)
        {
            var head = UiKit.El("sheet__head", sheet);
            var tile = UiKit.Frame("srow__tile", head);
            tile.Add(new UiIcon(glyph, ArcadeTheme.Pulse, 2f));
            UiKit.Text(title, "sheet__title f-display", head);

            var close = UiKit.Frame("sq", head);
            close.Add(new UiIcon(UiIcon.Glyph.Close, ArcadeTheme.Ink, 2.5f));
            UiKit.OnTap(close, () => CloseSheet(false), UiSound.Close);
        }

        /// <summary>A hexagon badge with a glyph in it, for the confirmation sheets.</summary>
        private static UiHex Badge(VisualElement parent, UiIcon.Glyph glyph, Color edge, Color fill)
        {
            var hex = new UiHex { Edge = edge, Fill = fill };
            hex.AddToClassList("sheet__badge");
            hex.Add(new UiIcon(glyph, edge, 2f));
            parent.Add(hex);
            return hex;
        }

        private VisualElement Buttons(VisualElement parent)
        {
            return UiKit.El("sheet__buttons", parent);
        }

        // ---- text fields ----

        /// <summary>
        /// A labelled text field. <paramref name="secret"/> fields are masked and get an eye button that
        /// shows or hides every secret field on the sheet at once.
        /// </summary>
        private UiFrame Field(VisualElement parent, string label, UiIcon.Glyph glyph, bool secret, string initial,
                              Action<string> onChange, out TextField input)
        {
            var wrap = UiKit.El("field-wrap", parent);
            UiKit.Text(label, "field-label f-body-semi", wrap);

            var frame = UiKit.Frame("field", wrap);
            frame.Add(new UiIcon(glyph, ArcadeTheme.Pulse, 2f));

            var field = new TextField
            {
                isPasswordField = secret && !showPasswords,
                maxLength = 30,
                value = initial,
                autoCorrection = false
            };
            field.AddToClassList("field__input");
            field.AddToClassList("f-body-semi");
            field.RegisterValueChangedCallback(e => onChange(e.newValue));
            field.RegisterCallback<FocusInEvent>(_ => frame.AddToClassList("is-focus"));
            field.RegisterCallback<FocusOutEvent>(_ => frame.RemoveFromClassList("is-focus"));
            frame.Add(field);

            if (secret)
            {
                secretFields.Add(field);

                var eye = UiKit.El("field__eye", frame);
                var eyeIcon = new UiIcon(UiIcon.Glyph.Eye, showPasswords ? ArcadeTheme.Pulse : ArcadeTheme.InkMuted, 2f);
                eye.Add(eyeIcon);
                eyeIcons.Add(eyeIcon);
                UiKit.OnTap(eye, ToggleShowPasswords, UiSound.Toggle);
            }

            input = field;
            return frame;
        }

        private void ToggleShowPasswords()
        {
            showPasswords = !showPasswords;
            foreach (var f in secretFields) f.isPasswordField = !showPasswords;
            foreach (var icon in eyeIcons) icon.Color = showPasswords ? ArcadeTheme.Pulse : ArcadeTheme.InkMuted;
        }

        private static bool[] Rules(string password, string confirm)
        {
            bool upper = false, lower = false, digit = false;
            foreach (char c in password)
            {
                if (char.IsUpper(c)) upper = true;
                else if (char.IsLower(c)) lower = true;
                else if (char.IsDigit(c)) digit = true;
            }

            return new[] { password.Length >= 8, upper, lower, digit, confirm.Length > 0 && confirm == password };
        }

        private static List<VisualElement> RuleChips(VisualElement parent)
        {
            var row = UiKit.El("rules", parent);
            var chips = new List<VisualElement>();
            foreach (string name in RuleNames)
            {
                var chip = UiKit.El("rule", row);
                UiKit.El("rule__dot", chip);
                UiKit.Text(name, "rule__label f-display", chip);
                chips.Add(chip);
            }
            return chips;
        }

        /// <summary>Lights the chips and marks the two fields. Returns whether every rule is met.</summary>
        private static bool ShowRules(List<VisualElement> chips, string password, string confirm,
                                      VisualElement passwordField, VisualElement confirmField)
        {
            bool[] met = Rules(password, confirm);
            bool all = true;
            for (int i = 0; i < chips.Count; i++)
            {
                chips[i].EnableInClassList("is-ok", met[i]);
                all &= met[i];
            }

            bool strong = met[0] && met[1] && met[2] && met[3];
            passwordField.EnableInClassList("is-ok", password.Length > 0 && strong);
            passwordField.EnableInClassList("is-warn", password.Length > 0 && !strong);
            confirmField.EnableInClassList("is-ok", confirm.Length > 0 && confirm == password);
            confirmField.EnableInClassList("is-bad", confirm.Length > 0 && confirm != password);
            return all;
        }

        // ---- change password ----

        private void OpenPasswordSheet()
        {
            currentPassword = newPassword = confirmPassword = string.Empty;
            showPasswords = false;
            passwordStep = 1;
            busy = false;

            var sheet = NewSheet(620);
            SheetHead(sheet, UiIcon.Glyph.Key, "CHANGE PASSWORD");

            var steps = UiKit.El("steps", sheet);
            var one = UiKit.El("step is-on", steps);
            UiKit.El("step__bar", one);
            UiKit.Text("1 · CURRENT", "step__label f-display", one);
            stepTwo = UiKit.El("step", steps);
            UiKit.El("step__bar", stepTwo);
            UiKit.Text("2 · NEW", "step__label f-display", stepTwo);

            passwordBody = UiKit.El(string.Empty, sheet);
            sheetStatus = UiKit.Text(string.Empty, "sheet__status f-body-semi", sheet);

            BuildPasswordStep();
            ShowSheet(sheet);
        }

        private void BuildPasswordStep()
        {
            passwordBody.Clear();
            secretFields.Clear();
            eyeIcons.Clear();
            sheetStatus.text = string.Empty;
            stepTwo.EnableInClassList("is-on", passwordStep == 2);

            if (passwordStep == 1)
            {
                UiFrame next = null;
                Field(passwordBody, "CURRENT PASSWORD", UiIcon.Glyph.Lock, true, currentPassword, v =>
                {
                    currentPassword = v;
                    if (next != null) next.EnableInClassList("is-disabled", v.Length == 0);
                }, out TextField _);

                var row = Buttons(passwordBody);
                UiKit.Button("CANCEL", "ghost", () => CloseSheet(false), row);
                next = (UiFrame)UiKit.Button("NEXT", "pulse", () =>
                {
                    passwordStep = 2;
                    BuildPasswordStep();
                }, row);
                next.EnableInClassList("is-disabled", currentPassword.Length == 0);
            }
            else
            {
                UiFrame save = null;
                List<VisualElement> chips = null;
                UiFrame newFrame = null, confirmFrame = null;

                void Refresh()
                {
                    bool ok = ShowRules(chips, newPassword, confirmPassword, newFrame, confirmFrame);
                    if (save != null) save.EnableInClassList("is-disabled", !ok || busy);
                }

                newFrame = Field(passwordBody, "NEW PASSWORD", UiIcon.Glyph.Lock, true, newPassword, v =>
                {
                    newPassword = v;
                    Refresh();
                }, out TextField _);
                confirmFrame = Field(passwordBody, "CONFIRM NEW PASSWORD", UiIcon.Glyph.Lock, true, confirmPassword, v =>
                {
                    confirmPassword = v;
                    Refresh();
                }, out TextField _);
                chips = RuleChips(passwordBody);

                var row = Buttons(passwordBody);
                UiKit.Button("BACK", "ghost", () =>
                {
                    passwordStep = 1;
                    BuildPasswordStep();
                }, row);
                save = (UiFrame)UiKit.Button("SAVE", "pulse", SavePassword, row);
                Refresh();
            }

            UiFonts.Apply(passwordBody);
        }

        private async void SavePassword()
        {
            if (busy) return;
            busy = true;
            sheetStatus.text = "SAVING…";

            bool ok = await PlayerAccount.ChangePasswordAsync(currentPassword, newPassword);
            busy = false;

            if (ok)
            {
                CloseSheet(false);
                Toast("PASSWORD CHANGED");
                GameSfx.PlayUi(UiSound.Reward);
            }
            else if (sheetStatus != null)
            {
                sheetStatus.text = PlayerAccount.LastError;
                GameSfx.PlayUi(UiSound.Deny);
            }
        }

        // ---- sign up ----

        private void OpenSignUpSheet()
        {
            string username = string.Empty;
            newPassword = confirmPassword = string.Empty;
            showPasswords = false;
            secretFields.Clear();
            eyeIcons.Clear();
            busy = false;

            var sheet = NewSheet(620);
            SheetHead(sheet, UiIcon.Glyph.AddPerson, "SIGN UP");

            UiFrame create = null;
            List<VisualElement> chips = null;
            UiFrame passwordFrame = null, confirmFrame = null;

            void Refresh()
            {
                bool rules = ShowRules(chips, newPassword, confirmPassword, passwordFrame, confirmFrame);
                if (create != null) create.EnableInClassList("is-disabled", !rules || username.Trim().Length < 3);
            }

            var body = UiKit.El(string.Empty, sheet);
            Field(body, "USERNAME", UiIcon.Glyph.Person, false, username, v =>
            {
                username = v;
                Refresh();
            }, out TextField _);
            passwordFrame = Field(body, "PASSWORD", UiIcon.Glyph.Lock, true, newPassword, v =>
            {
                newPassword = v;
                Refresh();
            }, out TextField _);
            confirmFrame = Field(body, "CONFIRM PASSWORD", UiIcon.Glyph.Lock, true, confirmPassword, v =>
            {
                confirmPassword = v;
                Refresh();
            }, out TextField _);
            chips = RuleChips(body);

            var row = Buttons(body);
            UiKit.Button("LATER", "ghost", () => CloseSheet(false), row);
            create = (UiFrame)UiKit.Button("CREATE", "pulse", async () =>
            {
                if (busy) return;
                busy = true;
                sheetStatus.text = "CREATING…";

                bool ok = await PlayerAccount.CreateAccountAsync(username, newPassword);
                busy = false;

                if (ok)
                {
                    CloseSheet(false);
                    Toast("ACCOUNT CREATED");
                    GameSfx.PlayUi(UiSound.Reward);
                }
                else if (sheetStatus != null)
                {
                    sheetStatus.text = PlayerAccount.LastError;
                    GameSfx.PlayUi(UiSound.Deny);
                }
            }, row);

            // A player who already has an account elsewhere signs in instead; without this a guest who
            // skipped the first-launch screen would have no way back to their account.
            var haveOne = UiKit.Button("ALREADY HAVE AN ACCOUNT?", "blue", OpenSignInSheet, body);
            haveOne.style.marginTop = 14f;

            sheetStatus = UiKit.Text(string.Empty, "sheet__status f-body-semi", sheet);
            Refresh();
            ShowSheet(sheet);
        }

        // ---- sign in ----

        private void OpenSignInSheet()
        {
            string username = string.Empty, password = string.Empty;
            showPasswords = false;
            secretFields.Clear();
            eyeIcons.Clear();
            busy = false;

            var sheet = NewSheet(620);
            SheetHead(sheet, UiIcon.Glyph.Person, "SIGN IN");

            UiFrame signIn = null;
            void Refresh()
            {
                if (signIn != null) signIn.EnableInClassList("is-disabled", username.Trim().Length == 0 || password.Length == 0);
            }

            var body = UiKit.El(string.Empty, sheet);
            Field(body, "USERNAME", UiIcon.Glyph.Person, false, username, v =>
            {
                username = v;
                Refresh();
            }, out TextField _);
            Field(body, "PASSWORD", UiIcon.Glyph.Lock, true, password, v =>
            {
                password = v;
                Refresh();
            }, out TextField _);

            // Said before it is done, not after: signing in swaps this phone's player for the other one.
            var warn = UiKit.Text("This replaces the player on this phone.", "sheet__line sheet__line--gold f-body-semi", body);
            warn.style.marginBottom = 4f;

            var row = Buttons(body);
            UiKit.Button("CANCEL", "ghost", () => CloseSheet(false), row);
            signIn = (UiFrame)UiKit.Button("SIGN IN", "pulse", async () =>
            {
                if (busy) return;
                busy = true;
                sheetStatus.text = "SIGNING IN…";

                bool ok = await PlayerAccount.SignInAsync(username, password);
                busy = false;

                if (ok)
                {
                    CloseSheet(false);
                    Toast("SIGNED IN");
                    GameSfx.PlayUi(UiSound.Reward);
                }
                else if (sheetStatus != null)
                {
                    sheetStatus.text = PlayerAccount.LastError;
                    GameSfx.PlayUi(UiSound.Deny);
                }
            }, row);

            sheetStatus = UiKit.Text(string.Empty, "sheet__status f-body-semi", sheet);
            Refresh();
            ShowSheet(sheet);
        }

        // ---- log out ----

        private void OpenLogOutSheet()
        {
            busy = false;
            var sheet = NewSheet(520);
            sheet.AddToClassList("sheet__center");
            Badge(sheet, UiIcon.Glyph.LogOut, ArcadeTheme.BlueLine, ArcadeTheme.Plate);
            UiKit.Text("LOG OUT?", "sheet__big f-display", sheet);
            UiKit.Text("Sign back in any time.", "sheet__line f-body-semi", sheet);

            var row = Buttons(sheet);
            row.style.alignSelf = Align.Stretch;
            UiKit.Button("STAY", "ghost", () => CloseSheet(false), row);
            UiKit.Button("LOG OUT", "pulse", async () =>
            {
                if (busy) return;
                busy = true;
                sheetStatus.text = "LOGGING OUT…";

                bool ok = await PlayerAccount.LogOutAsync();
                busy = false;

                if (ok)
                {
                    CloseSheet(false);
                    OnLoggedOut?.Invoke();
                }
                else if (sheetStatus != null)
                {
                    sheetStatus.text = PlayerAccount.LastError;
                    GameSfx.PlayUi(UiSound.Deny);
                }
            }, row);

            sheetStatus = UiKit.Text(string.Empty, "sheet__status f-body-semi", sheet);
            ShowSheet(sheet);
        }

        // ---- delete ----

        private void OpenDeleteSheet()
        {
            busy = false;
            // The same sheet as every other confirmation; the red is carried by the badge and the
            // hold button, which are the two things that mean "you lose something".
            var sheet = NewSheet(560);
            sheet.AddToClassList("sheet__center");
            Badge(sheet, UiIcon.Glyph.Trash, ArcadeTheme.Danger, ArcadeTheme.DangerFill);
            UiKit.Text("DELETE ACCOUNT?", "sheet__big f-display", sheet);

            // What goes with it, as pictures.
            var lost = UiKit.El("sheet__lost", sheet);
            LostItem(lost, UiIcon.Glyph.Coin, ArcadeTheme.Coin, "COINS");
            LostItem(lost, UiIcon.Glyph.Chest, ArcadeTheme.RarityEpic, "SKINS");
            LostItem(lost, UiIcon.Glyph.Levels, ArcadeTheme.Pulse, "LEVEL");

            var row = Buttons(sheet);
            row.style.alignSelf = Align.Stretch;
            row.style.marginTop = 26;
            UiKit.Button("KEEP IT", "pulse", () => CloseSheet(false), row);

            // A spacer rather than a margin: the two cut-corner buttons fit into each other's
            // diagonals, and with no real gap between them they read as overlapping.
            UiKit.El("sheet__gap", row);

            // Not a tap: a hold, so it cannot be done by accident or in passing.
            holdButton = UiKit.Frame("btn btn--danger hold", row);
            holdFill = UiKit.El("hold__fill", holdButton);
            holdFill.pickingMode = PickingMode.Ignore;
            holdButton.Add(new UiIcon(UiIcon.Glyph.Trash, ArcadeTheme.Danger, 2.2f));
            UiKit.Text("HOLD TO DELETE", "btn__label f-display", holdButton);
            holdButton.pickingMode = PickingMode.Position;
            holdButton.RegisterCallback<PointerDownEvent>(e =>
            {
                holdButton.CapturePointer(e.pointerId);
                StartHold();
            });
            holdButton.RegisterCallback<PointerUpEvent>(e =>
            {
                if (holdButton.HasPointerCapture(e.pointerId)) holdButton.ReleasePointer(e.pointerId);
                CancelHold();
            });
            holdButton.RegisterCallback<PointerCancelEvent>(e =>
            {
                if (holdButton.HasPointerCapture(e.pointerId)) holdButton.ReleasePointer(e.pointerId);
                CancelHold();
            });

            sheetStatus = UiKit.Text(string.Empty, "sheet__status f-body-semi", sheet);
            ShowSheet(sheet, UiSound.Warn);
        }

        private static void LostItem(VisualElement parent, UiIcon.Glyph glyph, Color color, string label)
        {
            var item = UiKit.El("sheet__lost-item", parent);
            item.Add(new UiIcon(glyph, color, 2f));
            UiKit.Text(label, "f-display", item).style.color = ArcadeTheme.InkMuted;
        }

        private void StartHold()
        {
            if (busy) return;
            CancelHold();
            holdStartedAt = Time.unscaledTime;
            holdQuarter = 0;
            GameSfx.PlayUi(UiSound.Step, 0.9f);
            holdItem = holdButton.schedule.Execute(HoldTick).Every(30);
        }

        private void HoldTick()
        {
            float progress = Mathf.Clamp01((Time.unscaledTime - holdStartedAt) / HoldSeconds);
            holdFill.style.width = Length.Percent(progress * 100f);

            int quarter = (int)(progress * 4f);
            if (quarter > holdQuarter && progress < 1f)
            {
                holdQuarter = quarter;
                GameSfx.PlayUi(UiSound.Step, 0.9f + 0.1f * quarter);
            }

            if (progress >= 1f)
            {
                holdItem.Pause();
                holdItem = null;
                DeleteAccount();
            }
        }

        private void CancelHold()
        {
            holdItem?.Pause();
            holdItem = null;
            if (holdFill != null && !busy) holdFill.style.width = Length.Percent(0f);
        }

        private async void DeleteAccount()
        {
            if (busy) return;
            busy = true;
            sheetStatus.text = "DELETING…";

            bool ok = await PlayerAccount.DeleteAccountAsync();
            busy = false;

            if (ok)
            {
                CloseSheet(false);
                Toast("ACCOUNT DELETED");
                GameSfx.PlayUi(UiSound.Warn);
            }
            else
            {
                if (holdFill != null) holdFill.style.width = Length.Percent(0f);
                if (sheetStatus != null) sheetStatus.text = PlayerAccount.LastError;
                GameSfx.PlayUi(UiSound.Deny);
            }
        }

        // ---------- toast ----------

        private void BuildToast()
        {
            var wrap = UiKit.El("toast-wrap", root);
            wrap.pickingMode = PickingMode.Ignore;
            toast = UiKit.Frame("toast", wrap);
            toast.pickingMode = PickingMode.Ignore;
            toast.Add(new UiIcon(UiIcon.Glyph.Check, ArcadeTheme.Pulse, 3f));
            toastLabel = UiKit.Text(string.Empty, "toast__label f-display", toast);
            UiKit.Show(toast, false);
        }

        private void Toast(string text)
        {
            toastLabel.text = text;
            UiKit.Show(toast, true);
            toast.style.opacity = 1f;
            toastItem?.Pause();
            toastItem = toast.schedule.Execute(() => UiKit.Show(toast, false)).StartingIn(1700);
        }
    }
}
