using System;
using System.Collections.Generic;
using TableFootball.Net;
using TableFootball.UI.Toolkit;
using UnityEngine;
using UnityEngine.UIElements;

namespace TableFootball.UI
{
    /// <summary>
    /// The wardrobe: every cosmetic the player owns, tabbed by kind, tap one to wear it. Opened from
    /// the profile's COLLECTION button and from the store's; Back returns to whichever it came from
    /// (<see cref="GameFlow"/> remembers). Built on UI Toolkit, styled by
    /// <c>Resources/UI/Styles/Collection.uss</c>.
    ///
    /// League badges are listed even when locked: a wardrobe that only showed what you had won would
    /// never tell a player what climbing the ladder is FOR. Skins that are not owned are not listed —
    /// they come out of Lucky Chests and the store's daily items.
    /// </summary>
    public class CollectionScreen : MonoBehaviour
    {
        private VisualElement root;
        private ScrollView scroll;
        private Label caption;
        private readonly List<VisualElement> tabs = new List<VisualElement>();
        private CosmeticKind tab = CosmeticKind.FieldSkin;

        /// <summary>Raised when the player backs out.</summary>
        public Action OnBack;

        public bool IsOpen => root != null && root.style.display != DisplayStyle.None;

        public void Build()
        {
            root = UiKit.El("screen collection", UiToolkitHost.Root, "Collection");
            var sheet = Resources.Load<StyleSheet>("UI/Styles/Collection");
            if (sheet != null) root.styleSheets.Add(sheet);
            root.pickingMode = PickingMode.Position;

            UiBackdrop.AddTo(root);

            var header = UiKit.El("coll__header", root);
            var back = UiKit.Frame("sq coll__back", header);
            back.Add(new UiIcon(UiIcon.Glyph.ChevronLeft, ArcadeTheme.Ink, 2.5f));
            UiKit.OnTap(back, () => OnBack?.Invoke(), UiSound.Back);

            var box = UiKit.El("coll__title-box", header);
            var row = UiKit.El("coll__title-row", box);
            row.Add(new UiIcon(UiIcon.Glyph.Chest, ArcadeTheme.Pulse, 2f));
            UiKit.Text("COLLECTION", "coll__title f-display", row);
            UiKit.El("coll__bar", box);

            var strip = UiKit.El("coll__tabs", root);
            for (int i = 0; i < CosmeticCatalog.TabKinds.Length; i++)
            {
                CosmeticKind kind = CosmeticCatalog.TabKinds[i];
                var t = UiKit.Frame("ctab", strip);
                t.Add(new UiIcon(UiArt.KindGlyph(kind), ArcadeTheme.Pulse, 2f));
                UiKit.Text(CosmeticCatalog.KindName(kind).ToUpperInvariant(), "ctab__label f-display", t);
                UiKit.OnTap(t, () => SelectTab(kind));
                tabs.Add(t);
            }

            caption = UiKit.Text(string.Empty, "coll__caption f-body-semi", root);

            scroll = new ScrollView(ScrollViewMode.Vertical) { name = "CollectionScroll" };
            scroll.AddToClassList("coll__scroll");
            scroll.horizontalScrollerVisibility = ScrollerVisibility.Hidden;
            scroll.verticalScrollerVisibility = ScrollerVisibility.Hidden;
            scroll.touchScrollBehavior = ScrollView.TouchScrollBehavior.Elastic;
            scroll.contentContainer.AddToClassList("coll__grid");
            root.Add(scroll);

            UiFonts.Apply(root);
            UiKit.Show(root, false);
        }

        /// <summary>Opens the wardrobe, on <paramref name="startTab"/> if one is given.</summary>
        public void Open(CosmeticKind? startTab = null)
        {
            if (root == null) return;

            if (startTab.HasValue) tab = startTab.Value;

            Inventory.OnChanged -= Redraw;
            Inventory.OnChanged += Redraw;

            UiKit.Show(root, true);
            root.BringToFront();
            root.style.opacity = 0f;
            UiKit.Fade(root, 1f, ArcadeTheme.TFast);
            GameSfx.PlayUi(UiSound.Open);

            SelectTab(tab, quiet: true);
        }

        public void Close()
        {
            Inventory.OnChanged -= Redraw;
            if (root != null) UiKit.Show(root, false);
        }

        private void OnDestroy() => Inventory.OnChanged -= Redraw;

        private void SelectTab(CosmeticKind kind, bool quiet = false)
        {
            tab = kind;
            for (int i = 0; i < tabs.Count; i++)
            {
                tabs[i].EnableInClassList("is-active", CosmeticCatalog.TabKinds[i] == kind);
            }

            caption.text = kind == CosmeticKind.Badge ? "Badges are earned in ranked and on the level road"
                         : kind == CosmeticKind.NameStyle ? "Name styles are earned on the level road"
                         : "Tap a skin to wear it";
            scroll.scrollOffset = Vector2.zero;
            Redraw();
        }

        private void Redraw()
        {
            if (!IsOpen) return;

            var grid = scroll.contentContainer;
            grid.Clear();

            bool earned = tab == CosmeticKind.Badge || tab == CosmeticKind.NameStyle;
            foreach (CosmeticItem item in CosmeticCatalog.OfKind(tab))
            {
                // What you own (the catalogue default counts: everybody owns it, and taking a skin OFF
                // means putting it back on), plus every earned item, locked, so the wardrobe shows what
                // climbing is FOR. Unowned skins come from chests.
                if (!earned && !item.IsDefault && !Inventory.Owns(item.Id)) continue;
                AddCard(grid, item);
            }

            UiFonts.Apply(grid);
        }

        private void AddCard(VisualElement grid, CosmeticItem item)
        {
            bool owned = Inventory.Owns(item.Id);
            bool equipped = Inventory.IsEquipped(item.Id);
            Color rarity = UIFactory.RarityColor(item.Rarity);

            var card = UiKit.Frame("ccard" + (equipped ? " is-equipped" : string.Empty), grid);
            card.OverrideEdge(rarity);

            var art = UiArt.Cosmetic(item);
            art.AddToClassList("ccard__art");
            if (!owned) art.style.opacity = 0.3f;
            card.Add(art);

            UiKit.Text(item.Name, "ccard__name f-display-semi", card);
            var rar = UiKit.Text(CosmeticCatalog.RarityName(item.Rarity).ToUpperInvariant(), "ccard__rarity f-display", card);
            rar.style.color = rarity;

            var foot = UiKit.El("ccard__foot", card);
            if (equipped)
            {
                foot.AddToClassList("ccard__foot--on");
                foot.style.backgroundColor = rarity;
                UiKit.Text("EQUIPPED", "ccard__foot-label ccard__foot-label--on f-display", foot);
            }
            else if (owned)
            {
                UiKit.Text("TAP TO WEAR", "ccard__foot-label f-display", foot);
            }
            else
            {
                var need = UiKit.Text(UnlockLine(item).ToUpperInvariant(), "ccard__foot-label f-display", foot);
                need.style.color = UnlockColor(item);
            }

            // Wearing something makes the confirm note; tapping what is already on, or a locked badge,
            // does not pretend to have done anything.
            UiSound sound = !owned ? UiSound.Deny : equipped ? UiSound.Tap : UiSound.Confirm;
            UiKit.OnTap(card, () => Tapped(item), sound);
        }

        /// <summary>What it takes to earn an item that is not owned yet: a level for the two level-road
        /// prizes, a league for the league badges.</summary>
        private static string UnlockLine(CosmeticItem item)
        {
            if (item.Id == LevelPath.GoldNameId) return "Level " + LevelPath.GoldNameLevel;
            if (item.Id == LevelPath.KingBadgeId) return "Level " + LevelPath.KingBadgeLevel;
            return "Reach " + Leagues.ShortName(LeagueBadges.LeagueOf(item.Id));
        }

        private static Color UnlockColor(CosmeticItem item) =>
            item.Id == LevelPath.GoldNameId || item.Id == LevelPath.KingBadgeId
                ? ArcadeTheme.Reward
                : UIFactory.LeagueColor(LeagueBadges.LeagueOf(item.Id));

        private void Tapped(CosmeticItem item)
        {
            bool badge = item.Kind == CosmeticKind.Badge;

            if (!Inventory.Owns(item.Id))
            {
                caption.text = "Reach " + UnlockLine(item).Replace("Reach ", string.Empty) + " to earn " +
                               (badge ? "this badge" : "this style");
                return;
            }

            if (Inventory.IsEquipped(item.Id))
            {
                // A badge is the one cosmetic with no default underneath it, so it is the only one that
                // can genuinely be taken OFF. A name style is changed by wearing Plain Name instead.
                if (badge)
                {
                    Inventory.Unequip(CosmeticKind.Badge);
                    caption.text = "Badge removed";
                }
                return;
            }

            Inventory.Equip(item.Id);
            caption.text = badge ? item.Name + " now shows beside your name" : item.Name + " equipped";
        }
    }
}
