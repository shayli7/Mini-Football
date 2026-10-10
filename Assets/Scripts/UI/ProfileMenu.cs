using System;
using System.Collections.Generic;
using TableFootball.Net;
using TableFootball.UI.Toolkit;
using UnityEngine;
using UnityEngine.UIElements;

namespace TableFootball.UI
{
    /// <summary>
    /// The profile: who you are (hex avatar, name, level and XP), the way into the Level Road and into
    /// Ranked, your record, and what you are wearing. Built on UI Toolkit, styled by
    /// <c>Resources/UI/Styles/Profile.uss</c>.
    ///
    /// The header carries the two ways out to the account's own screens: COLLECTION (the wardrobe) and
    /// the gear (Settings, which is where the account itself — sign up, password, log out, delete — now
    /// lives). Renaming is the only edit made here.
    ///
    /// It reports through callbacks and owns no sequencing; <see cref="GameFlow"/> decides where each
    /// one goes. Everything it knows about identity it asks <see cref="PlayerAccount"/>, and it redraws
    /// on the change events of the stores it reads.
    /// </summary>
    public class ProfileMenu : MonoBehaviour
    {
        private static readonly CosmeticKind[] WornKinds =
        {
            CosmeticKind.BallSkin, CosmeticKind.FigureSkin, CosmeticKind.FieldSkin, CosmeticKind.Background
        };

        private const int XpSegments = 10;

        private VisualElement root;

        private UiHex hex;
        private Label initial, levelPill, xpText;
        private UiShineLabel nameLabel;
        private VisualElement badgeSlot;
        private readonly List<VisualElement> segments = new List<VisualElement>();
        private VisualElement roadBadge;
        private Label roadBadgeLabel;
        private UiHex leagueHex;
        private Label leagueName, leagueNote;
        private Label playedValue, winsValue, rateValue, goalsValue;
        private VisualElement wearing;

        // Rename sheet
        private VisualElement sheetLayer;
        private TextField renameField;
        private Label renameStatus;
        private bool renaming;

        /// <summary>Raised when the player backs out.</summary>
        public Action OnBack;

        /// <summary>Raised by the gear in the top right. Settings has no other door.</summary>
        public Action OnOpenSettings;

        /// <summary>Raised by COLLECTION, or by tapping something worn (with that kind's tab).</summary>
        public Action<CosmeticKind?> OnOpenCollection;

        /// <summary>Raised by the Level Road button.</summary>
        public Action OnOpenLevelPath;

        /// <summary>Raised by the league row.</summary>
        public Action OnOpenRanked;

        public bool IsOpen => root != null && root.style.display != DisplayStyle.None;

        /// <summary><paramref name="canvasRoot"/> is the uGUI canvas the older screens hang off, unused
        /// here and kept so the boot order in TableFootballUI does not change.</summary>
        public void Build(Transform canvasRoot)
        {
            root = UiKit.El("screen profile", UiToolkitHost.Root, "Profile");
            var sheet = Resources.Load<StyleSheet>("UI/Styles/Profile");
            if (sheet != null) root.styleSheets.Add(sheet);
            root.pickingMode = PickingMode.Position;

            UiBackdrop.AddTo(root);

            BuildHeader();
            BuildIdentity();
            BuildRight();
            BuildRenameSheet();

            UiFonts.Apply(root);
            UiKit.Show(root, false);
        }

        // ---------- build ----------

        private void BuildHeader()
        {
            var header = UiKit.El("prof__header", root);

            var back = UiKit.Frame("sq prof__back", header);
            back.Add(new UiIcon(UiIcon.Glyph.ChevronLeft, ArcadeTheme.Ink, 2.5f));
            UiKit.OnTap(back, () => OnBack?.Invoke(), UiSound.Back);

            var box = UiKit.El("prof__title-box", header);
            UiKit.Text("PROFILE", "prof__title f-display", box);
            UiKit.El("prof__bar", box);

            var collection = UiKit.Frame("prof__collection", header);
            collection.Add(new UiIcon(UiIcon.Glyph.Chest, ArcadeTheme.Pulse, 2f));
            UiKit.Text("COLLECTION", "prof__collection-label f-display", collection);
            UiKit.OnTap(collection, () => OnOpenCollection?.Invoke(null));

            var gear = UiKit.Frame("sq sq--lit prof__gear", header);
            gear.Add(new UiIcon(UiIcon.Glyph.Gear, ArcadeTheme.Pulse, 2f));
            UiKit.OnTap(gear, () => OnOpenSettings?.Invoke());
        }

        private void BuildIdentity()
        {
            var panel = UiKit.Frame("frame--panel prof__panel", root);

            hex = new UiHex();
            hex.AddToClassList("prof__hex");
            initial = UiKit.Text("?", "prof__initial f-display", hex);
            panel.Add(hex);

            var nameRow = UiKit.El("prof__name-row", panel);
            // The badge worn beside the name, then the name itself (gold and shining, if that is worn).
            badgeSlot = UiKit.El("prof__badge-slot", nameRow);
            nameLabel = new UiShineLabel();
            nameLabel.AddToClassList("prof__name");
            nameLabel.AddToClassList("f-display");
            nameRow.Add(nameLabel);
            var rename = UiKit.Frame("sq sq--small", nameRow);
            rename.Add(new UiIcon(UiIcon.Glyph.Pencil, ArcadeTheme.Pulse, 2f));
            UiKit.OnTap(rename, OpenRenameSheet);

            var level = UiKit.El("prof__level", panel);
            var levelRow = UiKit.El("prof__level-row", level);
            levelPill = UiKit.Text("LV 1", "prof__pill f-display", levelRow);
            xpText = UiKit.Text(string.Empty, "prof__xp f-display-semi", levelRow);
            var bar = UiKit.El("prof__segments", level);
            for (int i = 0; i < XpSegments; i++) segments.Add(UiKit.El("prof__seg", bar));

            // The way into the Level Road: gold-edged, because it is where rewards wait.
            var road = UiKit.Frame("prof__road", panel);
            road.Add(new UiIcon(UiIcon.Glyph.Levels, ArcadeTheme.Reward, 2f));
            UiKit.Text("LEVEL ROAD", "prof__road-label f-display", road);
            roadBadge = UiKit.El("prof__road-badge", road);
            roadBadgeLabel = UiKit.Text("0", "prof__road-badge-label f-display", roadBadge);
            var roadArrow = new UiIcon(UiIcon.Glyph.ChevronRight, ArcadeTheme.Reward, 2.4f);
            roadArrow.AddToClassList("prof__arrow");
            road.Add(roadArrow);
            UiKit.OnTap(road, () => OnOpenLevelPath?.Invoke());

            // Your league, and where you sit in it.
            var league = UiKit.Frame("prof__league", panel);
            leagueHex = new UiHex();
            leagueHex.AddToClassList("prof__league-hex");
            leagueHex.Add(new UiIcon(UiIcon.Glyph.Trophy, ArcadeTheme.Reward, 2f));
            league.Add(leagueHex);
            var words = UiKit.El("prof__league-words", league);
            leagueName = UiKit.Text("RANKED", "prof__league-name f-display", words);
            leagueNote = UiKit.Text(string.Empty, "prof__league-note f-body-semi", words);
            var leagueArrow = new UiIcon(UiIcon.Glyph.ChevronRight, ArcadeTheme.InkMuted, 2.4f);
            leagueArrow.AddToClassList("prof__arrow");
            league.Add(leagueArrow);
            UiKit.OnTap(league, () => OnOpenRanked?.Invoke());
        }

        private void BuildRight()
        {
            var right = UiKit.El("prof__right", root);

            Heading(right, "RECORD");
            var tiles = UiKit.El("prof__tiles", right);
            playedValue = Tile(tiles, UiIcon.Glyph.Table, "MATCHES", false);
            winsValue = Tile(tiles, UiIcon.Glyph.Trophy, "WINS", true);
            rateValue = Tile(tiles, UiIcon.Glyph.Bolt, "WIN RATE", false);
            goalsValue = Tile(tiles, UiIcon.Glyph.Play, "GOALS", false);

            Heading(right, "WEARING");
            wearing = UiKit.El("prof__wearing", right);
        }

        private static void Heading(VisualElement parent, string text)
        {
            var head = UiKit.El("prof__heading", parent);
            UiKit.Text(text, "prof__heading-label f-display", head);
            UiKit.El("prof__heading-rule", head);
        }

        private static Label Tile(VisualElement parent, UiIcon.Glyph glyph, string caption, bool highlight)
        {
            var tile = UiKit.Frame("prof__tile", parent);
            tile.Add(new UiIcon(glyph, highlight ? ArcadeTheme.Pulse : ArcadeTheme.InkMuted, 2f));
            var value = UiKit.Text("0", "prof__tile-value f-display" + (highlight ? " prof__tile-value--lit" : string.Empty), tile);
            UiKit.Text(caption, "prof__tile-caption f-display-semi", tile);
            return value;
        }

        // ---------- rename ----------

        private void BuildRenameSheet()
        {
            sheetLayer = UiKit.El("bleed prof__sheet-layer", root);
            sheetLayer.pickingMode = PickingMode.Position;

            var sheet = UiKit.Frame("prof__sheet", sheetLayer);
            UiKit.Text("YOUR NAME", "prof__sheet-title f-display", sheet);

            var field = UiKit.Frame("prof__field", sheet);
            field.Add(new UiIcon(UiIcon.Glyph.Person, ArcadeTheme.Pulse, 2f));
            renameField = new TextField { maxLength = 20, autoCorrection = false };
            renameField.AddToClassList("prof__field-input");
            renameField.AddToClassList("f-body-semi");
            renameField.RegisterCallback<FocusInEvent>(_ => field.AddToClassList("is-focus"));
            renameField.RegisterCallback<FocusOutEvent>(_ => field.RemoveFromClassList("is-focus"));
            field.Add(renameField);

            var buttons = UiKit.El("prof__sheet-buttons", sheet);
            UiKit.Button("CANCEL", "ghost", () => CloseRenameSheet(), buttons);
            UiKit.Button("SAVE", "pulse", SaveName, buttons);

            renameStatus = UiKit.Text(string.Empty, "prof__sheet-status f-body-semi", sheet);
            UiKit.Show(sheetLayer, false);
        }

        private void OpenRenameSheet()
        {
            renameField.value = PlayerAccount.ShortName;
            renameStatus.text = string.Empty;
            renaming = false;
            UiFonts.Apply(sheetLayer);
            UiKit.Show(sheetLayer, true);
            sheetLayer.style.opacity = 0f;
            UiKit.Fade(sheetLayer, 1f, ArcadeTheme.TFast);
            GameSfx.PlayUi(UiSound.Open);
        }

        private void CloseRenameSheet()
        {
            UiKit.Show(sheetLayer, false);
        }

        private async void SaveName()
        {
            if (renaming) return;
            renaming = true;
            renameStatus.text = "SAVING…";

            bool ok = await PlayerAccount.SetNameAsync(renameField.value);
            renaming = false;

            if (ok)
            {
                CloseRenameSheet();
                Redraw();
            }
            else
            {
                renameStatus.text = PlayerAccount.LastError;
                GameSfx.PlayUi(UiSound.Deny);
            }
        }

        // ---------- state ----------

        public async void Open()
        {
            if (root == null) return;

            CloseRenameSheet();

            // Removed first, so reopening cannot stack a second handler onto a static event that
            // outlives this screen.
            Unsubscribe();
            PlayerAccount.OnChanged += Redraw;
            PlayerProgress.OnChanged += Redraw;
            Inventory.OnChanged += Redraw;
            LevelPath.OnChanged += Redraw;
            Ladder.OnChanged += Redraw;

            Redraw();

            UiKit.Show(root, true);
            root.BringToFront();
            root.style.opacity = 0f;
            UiKit.Fade(root, 1f, ArcadeTheme.TFast);
            GameSfx.PlayUi(UiSound.Open);

            // The name may not exist yet for a brand new player — the service mints one on the first
            // request — so the screen opens with what it has and fills itself in.
            await PlayerAccount.RefreshAsync();
            if (IsOpen) Redraw();
        }

        public void Close()
        {
            Unsubscribe();
            if (root != null) UiKit.Show(root, false);
        }

        private void OnDestroy() => Unsubscribe();

        private void Unsubscribe()
        {
            PlayerAccount.OnChanged -= Redraw;
            PlayerProgress.OnChanged -= Redraw;
            Inventory.OnChanged -= Redraw;
            LevelPath.OnChanged -= Redraw;
            Ladder.OnChanged -= Redraw;
        }

        private void Redraw()
        {
            if (root == null) return;

            string name = PlayerAccount.ShortName;
            bool hasName = !string.IsNullOrWhiteSpace(name);
            nameLabel.Set(hasName ? name : "—");
            badgeSlot.Clear();
            VisualElement worn = UiArt.WornBadge(34f);
            if (worn != null) badgeSlot.Add(worn);
            UiKit.Show(badgeSlot, worn != null);
            initial.text = hasName ? name.Substring(0, 1).ToUpperInvariant() : "?";

            levelPill.text = "LV " + PlayerProgress.Level;
            xpText.text = PlayerProgress.XpLabel;
            int lit = PlayerProgress.AtMaxLevel
                ? XpSegments
                : Mathf.Clamp(Mathf.RoundToInt(PlayerProgress.XpFraction * XpSegments), 0, XpSegments);
            for (int i = 0; i < segments.Count; i++) segments[i].EnableInClassList("is-lit", i < lit);

            int waiting = LevelPath.UnclaimedCount;
            UiKit.Show(roadBadge, waiting > 0);
            roadBadgeLabel.text = waiting > 9 ? "9+" : waiting.ToString();

            LadderStanding standing = Ladder.Standing;
            if (standing.Valid)
            {
                Color metal = UIFactory.LeagueColor(standing.League);
                leagueHex.Edge = metal;
                leagueHex.Q<UiIcon>().Color = metal;
                leagueName.text = Leagues.Name(standing.League).ToUpperInvariant();
                leagueName.style.color = metal;
                leagueNote.text = "#" + standing.RankInPod + " this week";
            }
            else
            {
                leagueHex.Edge = ArcadeTheme.BlueLine;
                leagueHex.Q<UiIcon>().Color = ArcadeTheme.InkMuted;
                leagueName.text = "RANKED";
                leagueName.style.color = ArcadeTheme.Ink;
                leagueNote.text = "Play online to get a rank";
            }

            // The overall progression record (vs-AI and online alike) — the same store the XP bar and
            // level read from, so the profile is internally consistent.
            playedValue.text = PlayerProgress.Played.ToString();
            winsValue.text = PlayerProgress.Wins.ToString();
            // A dash rather than 0% before the first match: nobody has a nought-percent win rate until
            // they have actually lost one.
            rateValue.text = PlayerProgress.WinPercent >= 0 ? PlayerProgress.WinPercent + "%" : "–";
            goalsValue.text = PlayerProgress.Goals.ToString();

            RedrawWearing();
        }

        private void RedrawWearing()
        {
            wearing.Clear();
            foreach (CosmeticKind kind in WornKinds)
            {
                CosmeticItem item = Inventory.Equipped(kind);
                if (!item.Valid) continue;

                Color rarity = UIFactory.RarityColor(item.Rarity);
                var card = UiKit.Frame("prof__worn", wearing);
                card.OverrideEdge(rarity);

                var art = UiArt.Cosmetic(item);
                art.AddToClassList("prof__worn-art");
                card.Add(art);

                UiKit.Text(item.Name, "prof__worn-name f-display-semi", card);
                var line = UiKit.Text(CosmeticCatalog.KindLabel(kind).ToUpperInvariant() + " · " +
                                      CosmeticCatalog.RarityName(item.Rarity).ToUpperInvariant(),
                                      "prof__worn-line f-display", card);
                line.style.color = rarity;

                CosmeticKind tapped = kind;
                UiKit.OnTap(card, () => OnOpenCollection?.Invoke(tapped));
            }

            UiFonts.Apply(wearing);
        }
    }
}
