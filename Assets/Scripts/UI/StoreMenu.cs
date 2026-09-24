using System;
using System.Collections.Generic;
using TableFootball.Net;
using TableFootball.UI.Toolkit;
using UnityEngine;
using UnityEngine.UIElements;

namespace TableFootball.UI
{
    /// <summary>
    /// The store: tabs of cosmetics, bought with the gold coins earned from ranked weeks and the level
    /// path. Built on UI Toolkit (see <see cref="UiToolkitHost"/>), styled by
    /// <c>Resources/UI/Styles/Store.uss</c>.
    ///
    /// It reads its data from <c>Net/</c> (<see cref="CosmeticCatalog"/>, <see cref="Inventory"/>,
    /// <see cref="Wallet"/>), redraws on their change events, and talks out only through
    /// <see cref="OnBack"/>. It holds no economy rules of its own: what an item costs and whether it
    /// can be afforded are questions for the wallet.
    ///
    /// A purchase asks first. One tap spending three thousand coins with no way back would make the
    /// grid dangerous to browse, and browsing is most of what a store is for.
    /// </summary>
    public class StoreMenu : MonoBehaviour
    {
        /// <summary>How long a status message stays up, in milliseconds.</summary>
        private const long StatusMillis = 2600;

        /// <summary>A card tap after the grid scrolled further than this is a scroll, not a tap.</summary>
        private const float ScrollTapSlop = 8f;

        private VisualElement root;
        private ScrollView grid;
        private Label coinLabel;
        private Label caption;
        private Label status;
        private VisualElement confirm;
        private Label confirmTitle;
        private Label confirmPrice;
        private readonly List<VisualElement> tabs = new List<VisualElement>();
        private IVisualElementScheduledItem statusClear;
        private Vector2 scrollAtPress;

        private CosmeticKind tab = CosmeticKind.FieldSkin;
        private CosmeticItem pendingBuy;

        /// <summary>Raised when the player backs out.</summary>
        public Action OnBack;

        public bool IsOpen => root != null && root.style.display != DisplayStyle.None;

        /// <summary>
        /// <paramref name="canvasRoot"/> is the uGUI canvas the other screens still hang off, unused
        /// here and kept so TableFootballUI's boot order does not change.
        /// </summary>
        public void Build(Transform canvasRoot)
        {
            root = UiKit.El("screen store", UiToolkitHost.Root, "StoreMenu");
            var sheet = Resources.Load<StyleSheet>("UI/Styles/Store");
            if (sheet != null) root.styleSheets.Add(sheet);
            root.pickingMode = PickingMode.Position;

            UiKit.El("bleed store__scrim", root).pickingMode = PickingMode.Ignore;
            var glow = UiKit.El("bleed store__glow", root);
            glow.pickingMode = PickingMode.Ignore;
            glow.style.backgroundImage = new StyleBackground(UiKit.TopGlow());

            var header = UiKit.Header(root, "STORE", Back);
            var right = UiKit.El("header__right", header);
            var coins = UiKit.El("chip", right);
            coins.Add(new UiIcon(UiIcon.Glyph.Coin, ArcadeTheme.Gold, 2f));
            coinLabel = UiKit.Text("0", "chip__value f-display", coins);

            BuildTabs();

            var info = UiKit.El("store__info", root);
            caption = UiKit.Text("COSMETICS", "store__caption f-body-semi", info);
            status = UiKit.Text(string.Empty, "store__status f-body-semi", info);

            grid = new ScrollView(ScrollViewMode.Vertical);
            grid.AddToClassList("store__grid");
            grid.contentContainer.AddToClassList("store__grid-content");
            grid.verticalScrollerVisibility = ScrollerVisibility.Hidden;
            grid.horizontalScrollerVisibility = ScrollerVisibility.Hidden;
            grid.touchScrollBehavior = ScrollView.TouchScrollBehavior.Clamped;
            // Recorded before any card sees the press, so a card can tell a tap from a drag.
            grid.RegisterCallback<PointerDownEvent>(_ => scrollAtPress = grid.scrollOffset, TrickleDown.TrickleDown);
            root.Add(grid);

            BuildConfirm();

            UiFonts.Apply(root);
            UiKit.Show(root, false);
        }

        // ---------- build ----------

        private void BuildTabs()
        {
            var row = UiKit.El("store__tabs", root);
            for (int i = 0; i < CosmeticCatalog.TabKinds.Length; i++)
            {
                CosmeticKind kind = CosmeticCatalog.TabKinds[i];
                var t = UiKit.El("store__tab", row);
                UiKit.Text(CosmeticCatalog.KindName(kind).ToUpperInvariant(), "store__tab-label f-display-semi", t);
                UiKit.OnTap(t, () =>
                {
                    tab = kind;
                    Redraw();
                    grid.scrollOffset = Vector2.zero;
                });
                t.userData = kind;
                tabs.Add(t);
            }
        }

        /// <summary>
        /// The purchase prompt: what, how much, and a way out. Built once and shown or hidden rather
        /// than created per purchase.
        /// </summary>
        private void BuildConfirm()
        {
            confirm = UiKit.El("store__confirm", root);
            confirm.pickingMode = PickingMode.Position;
            UiKit.El("bleed store__confirm-dim", confirm).pickingMode = PickingMode.Ignore;

            var panel = UiKit.El("panel store__confirm-panel", confirm);
            confirmTitle = UiKit.Text("Buy this?", "store__confirm-title f-display", panel);
            var price = UiKit.El("row store__confirm-price", panel);
            price.Add(new UiIcon(UiIcon.Glyph.Coin, ArcadeTheme.Gold, 2f));
            confirmPrice = UiKit.Text("0", "store__confirm-amount f-display", price);

            var buttons = UiKit.El("row store__confirm-buttons", panel);
            UiKit.Button("BUY", "gold", ConfirmBuy, buttons, "grow");
            UiKit.Button("CANCEL", "ghost", CancelBuy, buttons, "grow");

            UiKit.Show(confirm, false);
        }

        // ---------- the grid ----------

        private void Redraw()
        {
            if (grid == null) return;

            coinLabel.text = Wallet.Coins.ToString("N0");
            foreach (var t in tabs) t.EnableInClassList("is-selected", (CosmeticKind)t.userData == tab);

            bool badges = tab == CosmeticKind.Badge;
            // Says what the tab IS rather than what the screen is called. A shelf of badges under a
            // heading reading "cosmetics" invites exactly one question, and this answers it first.
            caption.text = badges ? "LEAGUE BADGES — EARNED, NOT SOLD" : "COSMETICS";

            grid.Clear();
            List<CosmeticItem> items = CosmeticCatalog.OfKind(tab);
            for (int i = 0; i < items.Count; i++)
            {
                // What is for sale, the catalogue default (owned by everybody, never priced — it has
                // to be reachable, because taking a bought skin off means putting the default back
                // on), and the badges, which are the wardrobe. Anything else is not shopping.
                CosmeticItem item = items[i];
                if (!item.IsForSale && !item.IsDefault && item.Kind != CosmeticKind.Badge) continue;
                grid.Add(Card(item));
            }

            UiFonts.Apply(grid);
        }

        /// <summary>
        /// One item card: art, name, rarity, and a price or its ownership state. The edge is the
        /// item's rarity colour, which is what makes a wall of cards scannable.
        /// </summary>
        private VisualElement Card(CosmeticItem item)
        {
            bool owned = Inventory.Owns(item.Id);
            bool equipped = Inventory.IsEquipped(item.Id);
            Color rarity = UIFactory.RarityColor(item.Rarity);

            var card = UiKit.El("store-card" + (equipped ? " is-equipped" : string.Empty));
            card.style.borderTopColor = card.style.borderBottomColor =
                card.style.borderLeftColor = card.style.borderRightColor = equipped ? rarity : rarity.WithAlpha(0.55f);
            UiKit.OnTap(card, () =>
            {
                if ((grid.scrollOffset - scrollAtPress).sqrMagnitude > ScrollTapSlop * ScrollTapSlop) return;
                Tapped(item);
            });

            var picture = UiKit.El("store-card__picture", card);
            picture.style.backgroundColor = Color.Lerp(ArcadeTheme.BgDeep, rarity, 0.16f);

            if (item.Kind == CosmeticKind.Badge)
            {
                // A badge draws itself — a ring in the league's metal round the trophy. Locked badges
                // are shown, not hidden: a wardrobe that only listed what you had won would never tell
                // a player what climbing the ladder is FOR.
                Color metal = UIFactory.LeagueColor(LeagueBadges.LeagueOf(item.Id));
                var ring = UiKit.El("store-card__badge", picture);
                ring.style.borderTopColor = ring.style.borderBottomColor =
                    ring.style.borderLeftColor = ring.style.borderRightColor = metal;
                ring.Add(new UiIcon(UiIcon.Glyph.Trophy, metal, 2f));
                if (!owned) ring.style.opacity = 0.3f;
            }
            else
            {
                // A real render of the real thing when one exists; the kind's icon when it does not.
                Sprite thumb = CosmeticThumbnails.For(item.Id);
                if (thumb != null)
                {
                    var art = UiKit.El("store-card__art", picture);
                    art.style.backgroundImage = new StyleBackground(thumb);
                }
                else
                {
                    var icon = new UiIcon(KindGlyph(item.Kind), rarity, 1.8f);
                    icon.AddToClassList("store-card__glyph");
                    picture.Add(icon);
                }
            }

            UiKit.Text(item.Name, "store-card__name f-display", card);
            var rar = UiKit.Text(CosmeticCatalog.RarityName(item.Rarity).ToUpperInvariant(), "store-card__rarity f-body-semi", card);
            rar.style.color = rarity;

            Footer(card, item, owned, equipped, rarity);

            if (equipped)
            {
                // "This is what you're wearing", readable before anything else on the card.
                var check = UiKit.El("store-card__check", card);
                check.Add(new UiIcon(UiIcon.Glyph.Check, ArcadeTheme.OnGold, 3f));
            }

            return card;
        }

        /// <summary>
        /// The bottom line of a card, which is the whole state machine of a store item in one row:
        /// a price when it can be bought, EQUIPPED when it is worn, TAP TO EQUIP when it is not.
        /// </summary>
        private static void Footer(VisualElement card, CosmeticItem item, bool owned, bool equipped, Color rarity)
        {
            var footer = UiKit.El("store-card__footer", card);

            if (equipped)
            {
                footer.style.backgroundColor = rarity.WithAlpha(0.2f);
                UiKit.Text("EQUIPPED", "store-card__state f-display", footer).style.color = rarity;
                return;
            }

            if (owned)
            {
                UiKit.Text("TAP TO EQUIP", "store-card__state f-body-semi", footer).style.color = ArcadeTheme.InkMuted;
                return;
            }

            // A badge has no price, because there is no price at which it can be had — it says which
            // league to reach instead, which is the only way to get one.
            if (item.Kind == CosmeticKind.Badge)
            {
                League league = LeagueBadges.LeagueOf(item.Id);
                UiKit.Text($"REACH {Leagues.Name(league).ToUpperInvariant()}", "store-card__state f-body-semi", footer)
                     .style.color = UIFactory.LeagueColor(league);
                return;
            }

            // Dimmed when it cannot be afforded, so the wall of prices sorts itself into "today" and
            // "later" without the player doing arithmetic against the balance in the header.
            bool affordable = Wallet.CanAfford(item.Price);
            footer.AddToClassList("row");
            if (!affordable) footer.style.opacity = 0.5f;
            footer.Add(new UiIcon(UiIcon.Glyph.Coin, affordable ? ArcadeTheme.Gold : ArcadeTheme.InkMuted, 2f));
            UiKit.Text(item.Price.ToString("N0"), "store-card__price f-display", footer)
                 .style.color = affordable ? ArcadeTheme.Gold : ArcadeTheme.InkMuted;
        }

        /// <summary>The icon standing in for a cosmetic with no rendered thumbnail. Shared with
        /// <see cref="ChestOpening"/>.</summary>
        internal static UiIcon.Glyph KindGlyph(CosmeticKind kind)
        {
            switch (kind)
            {
                case CosmeticKind.BallSkin: return UiIcon.Glyph.Coin;
                case CosmeticKind.FigureSkin: return UiIcon.Glyph.Person;
                case CosmeticKind.FieldSkin: return UiIcon.Glyph.Table;
                case CosmeticKind.TableSkin: return UiIcon.Glyph.Table;
                case CosmeticKind.Background: return UiIcon.Glyph.Globe;
                default: return UiIcon.Glyph.Star;
            }
        }

        // ---------- actions ----------

        private void Tapped(CosmeticItem item)
        {
            bool badge = item.Kind == CosmeticKind.Badge;

            if (Inventory.Owns(item.Id))
            {
                if (Inventory.IsEquipped(item.Id))
                {
                    // A badge is the one cosmetic with no default underneath it, so it is the only one
                    // that can genuinely be taken OFF. Every other kind always has something equipped.
                    if (badge)
                    {
                        Inventory.Unequip(CosmeticKind.Badge);
                        Say("Badge removed");
                    }

                    return;
                }

                Inventory.Equip(item.Id);
                Say(badge ? $"{item.Name} now shows beside your name" : $"{item.Name} equipped");
                return;
            }

            if (badge)
            {
                Say($"Reach {Leagues.Name(LeagueBadges.LeagueOf(item.Id))} league to earn this badge");
                return;
            }

            if (!Wallet.CanAfford(item.Price))
            {
                Say("Not enough coins — play ranked to earn more");
                return;
            }

            ShowConfirm(item);
        }

        private void Buy(CosmeticItem item)
        {
            // Asked and answered in one call: a separate "can I afford it" check followed by a
            // subtraction is two steps that can disagree, and the balance may have moved since the
            // confirm opened.
            if (!Wallet.TrySpend(item.Price))
            {
                Say("Not enough coins — play ranked to earn more");
                return;
            }

            Inventory.Grant(item.Id);
            // Worn straight away. Nobody buys a skin to leave it in a drawer.
            Inventory.Equip(item.Id);
            Say($"{item.Name} unlocked and equipped");
        }

        private void Say(string message)
        {
            if (status == null) return;
            status.text = message;
            statusClear?.Pause();
            statusClear = status.schedule.Execute(() => status.text = string.Empty).StartingIn(StatusMillis);
        }

        // ---------- confirm ----------

        private void ShowConfirm(CosmeticItem item)
        {
            pendingBuy = item;
            confirmTitle.text = $"Buy {item.Name}?";
            confirmPrice.text = item.Price.ToString("N0");
            UiKit.Show(confirm, true);
            confirm.BringToFront();
            UiKit.Enter(confirm.Q(className: "store__confirm-panel"));
        }

        private void ConfirmBuy()
        {
            CosmeticItem item = pendingBuy;
            CancelBuy();
            if (item.Valid) Buy(item);
        }

        private void CancelBuy()
        {
            pendingBuy = default;
            UiKit.Show(confirm, false);
        }

        // ---------- open / close ----------

        public void Open()
        {
            if (root == null) return;

            // Removed first, so reopening cannot stack a second handler onto a static event.
            Wallet.OnChanged -= Redraw;
            Wallet.OnChanged += Redraw;
            Inventory.OnChanged -= Redraw;
            Inventory.OnChanged += Redraw;

            CancelBuy();
            status.text = string.Empty;
            Redraw();

            UiKit.Show(root, true);
            root.BringToFront();
            root.style.opacity = 0f;
            UiKit.Fade(root, 1f, ArcadeTheme.TFast);
            UiKit.Enter(grid, 0.06f);
        }

        public void Close()
        {
            Wallet.OnChanged -= Redraw;
            Inventory.OnChanged -= Redraw;
            if (root != null) UiKit.Show(root, false);
        }

        private void OnDestroy()
        {
            Wallet.OnChanged -= Redraw;
            Inventory.OnChanged -= Redraw;
        }

        private void Back()
        {
            Close();
            OnBack?.Invoke();
        }
    }
}
