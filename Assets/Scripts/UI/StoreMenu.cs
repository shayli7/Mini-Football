using System;
using System.Collections.Generic;
using System.Globalization;
using TableFootball.Net;
using TableFootball.UI.Toolkit;
using UnityEngine;
using UnityEngine.UIElements;

namespace TableFootball.UI
{
    /// <summary>
    /// The store: one horizontal shelf in four sections, with a bar along the bottom that jumps between
    /// them and lights the one in view.
    ///
    ///   1. Lucky Chests — the four tiers, bought with coins at <see cref="ChestLoot.Price"/>.
    ///   2. Daily reward — a free Common or Rare chest for one ad, once a day.
    ///   3. Coins — a top-up paid for by watching ads, a few times a day.
    ///   4. Daily items — three skins on sale for coins, a new three every day.
    ///
    /// The wardrobe is not part of the store any more: COLLECTION in the header opens
    /// <see cref="CollectionScreen"/> and Back from there returns here (<see cref="GameFlow"/> keeps
    /// track). Built on UI Toolkit, styled by <c>Resources/UI/Styles/Store.uss</c>.
    ///
    /// Like every other screen it reads its data from <c>Net/</c> (<see cref="ChestLoot"/>,
    /// <see cref="ShopOffers"/>, <see cref="Wallet"/>, <see cref="Inventory"/>), redraws on their change
    /// events, and talks out only through callbacks. It holds no economy rules of its own.
    ///
    /// A purchase asks first. One tap spending three thousand coins with no way back would make the
    /// shelf dangerous to browse.
    /// </summary>
    public class StoreMenu : MonoBehaviour
    {
        private const int SectionCount = 4;
        private const int ChestSection = 0, DealSection = 1, CoinSection = 2, ItemSection = 3;

        private static readonly string[] SectionNames = { "LUCKY CHESTS", "DAILY REWARD", "COINS", "DAILY ITEMS" };
        private static readonly UiIcon.Glyph[] SectionGlyphs =
            { UiIcon.Glyph.Chest, UiIcon.Glyph.Clock, UiIcon.Glyph.Coin, UiIcon.Glyph.Store };

        private VisualElement root;
        private UiCoinChip coinChip;
        private ScrollView shelf;
        private readonly VisualElement[] sections = new VisualElement[SectionCount];
        private readonly List<VisualElement> navTabs = new List<VisualElement>();
        private VisualElement dealDot;
        private IVisualElementScheduledItem scrollAnim;
        private int activeSection = -1;

        private readonly VisualElement[] chestBuy = new VisualElement[4];

        // Daily reward
        private UiFrame dealCard;
        private VisualElement dealHalo, dealArt, dealButton;
        private Label dealTierLabel, dealName, dealCountdown, dealButtonLabel;
        private ChestTier shownDealTier = (ChestTier)(-1);

        // Coins
        private readonly List<VisualElement> adPips = new List<VisualElement>();
        private Label adsLeftLabel, adButtonLabel;
        private VisualElement adButton;

        // Daily items
        private VisualElement itemsRow;
        private Label itemsCountdown;
        private string itemsSignature = string.Empty;
        private readonly List<(CosmeticItem item, VisualElement buy)> itemBuys =
            new List<(CosmeticItem, VisualElement)>();

        // Toast
        private Label toast;
        private int toastToken;

        // Buy confirmation
        private VisualElement sheetLayer, sheetArt;
        private Label sheetTitle, sheetPrice, sheetGo;
        private Action pendingBuy;

        private float nextTick;

        /// <summary>Raised when the player backs out.</summary>
        public Action OnBack;

        /// <summary>Raised by COLLECTION in the header.</summary>
        public Action OnOpenCollection;

        public bool IsOpen => root != null && root.style.display != DisplayStyle.None;

        /// <summary><paramref name="canvasRoot"/> is the uGUI canvas the older screens hang off, unused
        /// here and kept so the boot order in TableFootballUI does not change.</summary>
        public void Build(Transform canvasRoot)
        {
            root = UiKit.El("screen store", UiToolkitHost.Root, "Store");
            var sheet = Resources.Load<StyleSheet>("UI/Styles/Store");
            if (sheet != null) root.styleSheets.Add(sheet);
            root.pickingMode = PickingMode.Position;

            UiBackdrop.AddTo(root);

            BuildHeader();
            BuildShelf();
            BuildNav();

            toast = UiKit.Text(string.Empty, "st__toast f-display-semi", root);
            toast.pickingMode = PickingMode.Ignore;
            toast.style.opacity = 0f;

            BuildSheet();

            UiFonts.Apply(root);
            UiKit.Show(root, false);
        }

        // ---------- build ----------

        private void BuildHeader()
        {
            var header = UiKit.El("st__header", root);

            var back = UiKit.Frame("sq st__back", header);
            back.Add(new UiIcon(UiIcon.Glyph.ChevronLeft, ArcadeTheme.Ink, 2.5f));
            UiKit.OnTap(back, Back, UiSound.Back);

            var box = UiKit.El("st__title-box", header);
            var row = UiKit.El("st__title-row", box);
            row.Add(new UiIcon(UiIcon.Glyph.Store, ArcadeTheme.Pulse, 2f));
            UiKit.Text("STORE", "st__title f-display", row);
            UiKit.El("st__bar", box);

            var tools = UiKit.El("st__tools", header);
            var collection = UiKit.Frame("st__collection", tools);
            collection.Add(new UiIcon(UiIcon.Glyph.Person, ArcadeTheme.Pulse, 2f));
            UiKit.Text("COLLECTION", "st__collection-label f-display", collection);
            UiKit.OnTap(collection, () => OnOpenCollection?.Invoke());
            coinChip = new UiCoinChip();
            tools.Add(coinChip);
        }

        private void BuildShelf()
        {
            shelf = new ScrollView(ScrollViewMode.Horizontal) { name = "StoreShelf" };
            shelf.AddToClassList("st__shelf");
            shelf.horizontalScrollerVisibility = ScrollerVisibility.Hidden;
            shelf.verticalScrollerVisibility = ScrollerVisibility.Hidden;
            shelf.touchScrollBehavior = ScrollView.TouchScrollBehavior.Elastic;
            shelf.contentContainer.AddToClassList("st__sections");
            shelf.horizontalScroller.valueChanged += _ => UpdateActiveSection();
            root.Add(shelf);

            var chests = Section(ChestSection, out _);
            for (int i = 0; i < 4; i++) BuildChestCard(chests, (ChestTier)i);

            Divider();
            BuildDealCard(Section(DealSection, out _));

            Divider();
            BuildCoinsCard(Section(CoinSection, out _));

            Divider();
            itemsRow = Section(ItemSection, out itemsCountdown);
        }

        /// <summary>A heading over a row of cards. Returns the row to put the cards in.</summary>
        private VisualElement Section(int index, out Label rightNote)
        {
            var col = UiKit.El("st__section", shelf.contentContainer);
            sections[index] = col;

            var head = UiKit.El("st__heading", col);
            head.Add(new UiIcon(SectionGlyphs[index], ArcadeTheme.Pulse, 2f));
            UiKit.Text(SectionNames[index], "st__heading-label f-display", head);
            UiKit.El("st__heading-rule", head);
            rightNote = null;
            if (index == ItemSection || index == DealSection)
            {
                var clock = UiKit.El("st__heading-timer", head);
                clock.Add(new UiIcon(UiIcon.Glyph.Clock, ArcadeTheme.InkMuted, 2f));
                rightNote = UiKit.Text(string.Empty, "st__heading-time f-display-semi", clock);
            }

            if (index == DealSection) dealCountdown = rightNote;

            return UiKit.El("st__cards", col);
        }

        private void Divider() => UiKit.El("st__divider", shelf.contentContainer);

        private void BuildChestCard(VisualElement parent, ChestTier tier)
        {
            Color tint = UIFactory.ChestColor(tier);
            var card = UiKit.Frame("st__card st__card--chest", parent);
            card.OverrideEdge(new Color(tint.r, tint.g, tint.b, 0.6f));

            var tag = UiKit.Text(CosmeticCatalog.RarityName((Rarity)(int)tier).ToUpperInvariant(), "st__tag f-display", card);
            tag.style.color = tint;

            var stage = UiKit.El("st__stage", card);
            var halo = UiKit.El("st__halo", stage);
            halo.style.backgroundColor = new Color(tint.r, tint.g, tint.b, 0.13f);
            var chest = new UiChest(tier);
            chest.AddToClassList("st__chest");
            stage.Add(chest);

            UiKit.Text("LUCKY CHEST", "st__name f-display", card);

            // What it leans toward: one dot per rarity, brighter the likelier.
            var dots = UiKit.El("st__odds", card);
            for (int r = 0; r < 4; r++)
            {
                int chance = ChestLoot.Chance(tier, (Rarity)r);
                var dot = UiKit.El("st__odd", dots);
                Color c = UIFactory.RarityColor((Rarity)r);
                dot.style.backgroundColor = c;
                dot.style.opacity = chance <= 0 ? 0.14f : Mathf.Lerp(0.45f, 1f, Mathf.Clamp01(chance / 60f));
            }

            ChestTier captured = tier;
            chestBuy[(int)tier] = PriceButton(card, ChestLoot.Price(tier), () => AskBuyChest(captured));
        }

        private void BuildDealCard(VisualElement parent)
        {
            dealCard = UiKit.Frame("st__card st__card--deal", parent);
            dealCard.OverrideEdge(ArcadeTheme.Reward);

            dealHalo = UiKit.El("st__glow", dealCard);
            dealHalo.pickingMode = PickingMode.Ignore;
            dealHalo.SendToBack();

            var free = UiKit.Text("FREE", "st__free f-display", dealCard);
            free.pickingMode = PickingMode.Ignore;
            dealTierLabel = UiKit.Text(string.Empty, "st__tag st__tag--right f-display", dealCard);

            var stage = UiKit.El("st__stage", dealCard);
            UiKit.El("st__halo st__halo--gold", stage);
            dealArt = UiKit.El("st__deal-art", stage);

            dealName = UiKit.Text(string.Empty, "st__name f-display", dealCard);
            UiKit.Text("ONE AD · ONE CHEST · EVERY DAY", "st__blurb f-display-semi", dealCard);

            dealButton = UiKit.Frame("btn btn--reward st__buy st__watch", dealCard);
            dealButton.Add(new UiIcon(UiIcon.Glyph.Play, ArcadeTheme.OnReward, 2.4f));
            dealButtonLabel = UiKit.Text("WATCH AD", "btn__label f-display", dealButton);
            UiKit.OnTap(dealButton, ClaimDeal, UiSound.Reward);
        }

        private void BuildCoinsCard(VisualElement parent)
        {
            var card = UiKit.Frame("st__card st__card--coins", parent);
            card.OverrideEdge(new Color(ArcadeTheme.Coin.r, ArcadeTheme.Coin.g, ArcadeTheme.Coin.b, 0.5f));

            var stage = UiKit.El("st__stage", card);
            UiKit.El("st__halo st__halo--gold", stage);
            var pile = UiKit.El("st__pile", stage);
            var a = new UiCoin(); a.AddToClassList("st__pile-coin"); a.AddToClassList("st__pile-coin--l"); pile.Add(a);
            var b = new UiCoin(); b.AddToClassList("st__pile-coin"); b.AddToClassList("st__pile-coin--r"); pile.Add(b);
            var c = new UiCoin(); c.AddToClassList("st__pile-coin"); c.AddToClassList("st__pile-coin--m"); pile.Add(c);

            UiKit.Text("+" + ShopOffers.AdCoins, "st__amount f-display", card);
            UiKit.Text("COINS", "st__name f-display", card);

            var pips = UiKit.El("st__pips", card);
            for (int i = 0; i < ShopOffers.AdsPerDay; i++) adPips.Add(UiKit.El("st__pip", pips));
            adsLeftLabel = UiKit.Text(string.Empty, "st__left f-display-semi", card);

            adButton = UiKit.Frame("btn btn--reward st__buy st__watch", card);
            adButton.Add(new UiIcon(UiIcon.Glyph.Play, ArcadeTheme.OnReward, 2.4f));
            adButtonLabel = UiKit.Text("WATCH AD", "btn__label f-display", adButton);
            UiKit.OnTap(adButton, WatchCoinAd, UiSound.Reward);
        }

        /// <summary>The blue-outlined button with the coin and the price. Greyed (not disabled) when the
        /// player cannot afford it, so tapping it can still say why.</summary>
        private static VisualElement PriceButton(VisualElement parent, int price, Action onTap)
        {
            var buy = UiKit.Frame("btn btn--pulse st__buy", parent);
            buy.Add(new UiCoin());
            UiKit.Text(price.ToString("N0", CultureInfo.InvariantCulture), "btn__label f-display", buy);
            UiKit.OnTap(buy, onTap);
            return buy;
        }

        private void BuildNav()
        {
            var bar = UiKit.El("st__nav", root);
            for (int i = 0; i < SectionCount; i++)
            {
                int index = i;
                var tab = UiKit.Frame("st__tab", bar);
                tab.Add(new UiIcon(SectionGlyphs[i], ArcadeTheme.InkMuted, 2f));
                UiKit.Text(SectionNames[i], "st__tab-label f-display", tab);
                UiKit.OnTap(tab, () => JumpTo(index));
                navTabs.Add(tab);

                // A gold dot on the daily reward tab while the free chest is waiting.
                if (i == DealSection)
                {
                    dealDot = UiKit.El("st__dot", tab);
                    dealDot.pickingMode = PickingMode.Ignore;
                }
            }
        }

        private void BuildSheet()
        {
            sheetLayer = UiKit.El("bleed st__sheet-layer", root);
            sheetLayer.pickingMode = PickingMode.Position;

            var sheet = UiKit.Frame("st__sheet", sheetLayer);
            sheetArt = UiKit.El("st__sheet-art", sheet);
            sheetTitle = UiKit.Text(string.Empty, "st__sheet-title f-display", sheet);

            var price = UiKit.El("st__sheet-price", sheet);
            price.Add(new UiCoin());
            sheetPrice = UiKit.Text("0", "st__sheet-amount f-display", price);

            var buttons = UiKit.El("st__sheet-buttons", sheet);
            UiKit.Button("CANCEL", "ghost", CloseSheet, buttons);
            var go = UiKit.Button("OPEN", "pulse", ConfirmSheet, buttons);
            sheetGo = go.Q<Label>();

            UiKit.Show(sheetLayer, false);
        }

        // ---------- sections ----------

        private void JumpTo(int index)
        {
            SelectSection(index);
            float target = Mathf.Max(0f, sections[index].layout.x - 40f);

            scrollAnim?.Pause();
            if (ArcadeTheme.ReducedMotion)
            {
                shelf.scrollOffset = new Vector2(target, 0f);
                return;
            }

            float from = shelf.scrollOffset.x;
            float start = Time.unscaledTime;
            scrollAnim = shelf.schedule.Execute(() =>
            {
                float t = Mathf.Clamp01((Time.unscaledTime - start) / 0.28f);
                float k = 1f - (1f - t) * (1f - t);
                shelf.scrollOffset = new Vector2(Mathf.Lerp(from, target, k), 0f);
                if (t >= 1f) scrollAnim.Pause();
            }).Every(16);
        }

        /// <summary>Lights the bar's tab for the section that is in view.</summary>
        private void UpdateActiveSection()
        {
            float x = shelf.scrollOffset.x;
            int found = 0;
            for (int i = 0; i < SectionCount; i++)
            {
                if (sections[i].layout.x - 90f <= x) found = i;
            }

            // The last section may be too short to ever reach its own start; the end of the shelf is it.
            if (x >= shelf.horizontalScroller.highValue - 2f && shelf.horizontalScroller.highValue > 0f)
            {
                found = SectionCount - 1;
            }

            SelectSection(found);
        }

        private void SelectSection(int index)
        {
            if (index == activeSection) return;
            activeSection = index;
            for (int i = 0; i < navTabs.Count; i++)
            {
                bool on = i == index;
                navTabs[i].EnableInClassList("is-active", on);
                navTabs[i].Q<UiIcon>().Color = on ? ArcadeTheme.Pulse : ArcadeTheme.InkMuted;
            }
        }

        // ---------- refresh ----------

        /// <summary>Repaints the shelf's live state: what can be afforded, and what the offers have left.</summary>
        private void RefreshShelf()
        {
            if (root == null) return;

            for (int i = 0; i < chestBuy.Length; i++)
            {
                chestBuy[i].EnableInClassList("is-poor", !Wallet.CanAfford(ChestLoot.Price((ChestTier)i)));
            }

            RefreshDeal();
            RefreshAds();
            RefreshItems();
            RefreshCountdowns();
        }

        private void RefreshDeal()
        {
            ChestTier tier = ShopOffers.DealTier;
            if (tier != shownDealTier)
            {
                shownDealTier = tier;
                dealArt.Clear();
                var chest = new UiChest(tier);
                chest.AddToClassList("st__chest");
                dealArt.Add(chest);
                dealTierLabel.text = CosmeticCatalog.RarityName((Rarity)(int)tier).ToUpperInvariant();
                dealTierLabel.style.color = UIFactory.ChestColor(tier);
                dealName.text = "LUCKY CHEST";
            }

            bool open = !ShopOffers.DealClaimed;
            dealButton.EnableInClassList("is-disabled", !open);
            dealButtonLabel.text = open ? "WATCH AD" : "CLAIMED";
            UiKit.Show(dealHalo, open);
            UiKit.Show(dealDot, open);
        }

        private void RefreshAds()
        {
            int left = ShopOffers.AdsLeft;
            for (int i = 0; i < adPips.Count; i++) adPips[i].EnableInClassList("is-lit", i < left);
            adsLeftLabel.text = left + " OF " + ShopOffers.AdsPerDay + " LEFT";
            adButton.EnableInClassList("is-disabled", left <= 0);
            adButtonLabel.text = left > 0 ? "WATCH AD" : "BACK TOMORROW";
        }

        private void RefreshItems()
        {
            CosmeticItem[] today = ShopOffers.DailyItems;

            string signature = string.Empty;
            foreach (CosmeticItem item in today) signature += item.Id + (Inventory.Owns(item.Id) ? "+" : "-") + ";";

            if (signature != itemsSignature)
            {
                itemsSignature = signature;
                itemsRow.Clear();
                itemBuys.Clear();
                foreach (CosmeticItem item in today) BuildItemCard(item);
                if (today.Length == 0) UiKit.Text("YOU OWN EVERYTHING ON SALE", "st__empty f-display-semi", itemsRow);
                UiFonts.Apply(itemsRow);
            }

            foreach (var entry in itemBuys)
            {
                entry.buy.EnableInClassList("is-poor", !Wallet.CanAfford(ShopOffers.DailyItemPrice(entry.item)));
            }
        }

        private void BuildItemCard(CosmeticItem item)
        {
            Color rarity = UIFactory.RarityColor(item.Rarity);
            bool owned = Inventory.Owns(item.Id);

            var card = UiKit.Frame("st__card st__card--item", itemsRow);
            card.OverrideEdge(new Color(rarity.r, rarity.g, rarity.b, owned ? 0.3f : 0.6f));

            var tag = UiKit.Text(CosmeticCatalog.RarityName(item.Rarity).ToUpperInvariant(), "st__tag f-display", card);
            tag.style.color = rarity;

            var art = UiArt.Cosmetic(item);
            art.AddToClassList("st__item-art");
            if (owned) art.style.opacity = 0.5f;
            card.Add(art);

            UiKit.Text(item.Name, "st__name st__name--item f-display", card);
            var kind = UiKit.Text(CosmeticCatalog.KindLabel(item.Kind).ToUpperInvariant(), "st__kind f-display-semi", card);
            kind.style.color = ArcadeTheme.InkMuted;

            if (owned)
            {
                var done = UiKit.Frame("btn btn--ghost st__buy st__owned", card);
                done.Add(new UiIcon(UiIcon.Glyph.Check, ArcadeTheme.Pulse, 2.6f));
                UiKit.Text("OWNED", "btn__label f-display", done);
                return;
            }

            CosmeticItem captured = item;
            var buy = PriceButton(card, ShopOffers.DailyItemPrice(item), () => AskBuyItem(captured));
            itemBuys.Add((item, buy));
        }

        private void RefreshCountdowns()
        {
            TimeSpan t = ShopOffers.UntilReset;
            string text = ((int)t.TotalHours).ToString("00") + ":" + t.Minutes.ToString("00") + ":" + t.Seconds.ToString("00");
            if (dealCountdown != null) dealCountdown.text = text;
            if (itemsCountdown != null) itemsCountdown.text = text;
        }

        private void Update()
        {
            if (!IsOpen || Time.unscaledTime < nextTick) return;
            nextTick = Time.unscaledTime + 1f;

            // Once a second: the countdowns, and — reading the offers is what rolls them over — a
            // screen left open past midnight picks up the new day's deal, ads and items.
            bool dealShown = !dealButton.ClassListContains("is-disabled");
            bool dealNow = !ShopOffers.DealClaimed;
            if (dealShown != dealNow || ShopOffers.DealTier != shownDealTier ||
                (ShopOffers.AdsLeft > 0) == adButton.ClassListContains("is-disabled"))
            {
                RefreshShelf();
                return;
            }

            RefreshCountdowns();
        }

        // ---------- actions ----------

        private void AskBuyChest(ChestTier tier)
        {
            int price = ChestLoot.Price(tier);
            if (!Wallet.CanAfford(price))
            {
                Poor();
                return;
            }

            ShowSheet(price, "OPEN " + ChestLoot.TierName(tier).ToUpperInvariant() + "?", "OPEN",
                      () => new UiChest(tier) { name = "sheetchest" }, () =>
                      {
                          // Asked and answered in one call: the balance may have moved since the sheet opened.
                          if (!Wallet.TrySpend(price)) { Poor(); return; }
                          OpenChest(ChestLoot.Open(tier));
                      });
        }

        private void AskBuyItem(CosmeticItem item)
        {
            int price = ShopOffers.DailyItemPrice(item);
            if (!Wallet.CanAfford(price))
            {
                Poor();
                return;
            }

            ShowSheet(price, "BUY " + item.Name.ToUpperInvariant() + "?", "BUY", () => UiArt.Cosmetic(item), () =>
            {
                if (ShopOffers.BuyDailyItem(item.Id))
                {
                    GameSfx.PlayUi(UiSound.Reward);
                    Say(item.Name.ToUpperInvariant() + " IS YOURS · WEAR IT IN COLLECTION");
                }
                else
                {
                    Poor();
                }
            });
        }

        private void Poor()
        {
            GameSfx.PlayUi(UiSound.Deny);
            Say("NOT ENOUGH COINS · PLAY RANKED OR WATCH AN AD");
        }

        private void ClaimDeal()
        {
            ShopOffers.ClaimDeal(drop =>
            {
                if (drop.HasValue) OpenChest(drop.Value);
                else Say("THE AD DID NOT FINISH · YOUR CHEST IS STILL WAITING");
            });
        }

        private void WatchCoinAd()
        {
            ShopOffers.WatchCoinAd(paid =>
            {
                if (paid) RewardFly.Play(RewardFly.Kind.Coins, adButton, coinChip, ShopOffers.AdCoins);
                Say(paid ? "+" + ShopOffers.AdCoins + " COINS ADDED" : "THE AD DID NOT FINISH · NO COINS THIS TIME");
            });
        }

        /// <summary>Plays the chest opening over the store for a chest that has already been rolled and
        /// granted. It draws on top of everything and returns to the store when the player collects. A
        /// chest with nothing left to win pays coins instead: the balance is held until the chest has
        /// closed, then the coins fly in.</summary>
        private void OpenChest(ChestDrop drop)
        {
            if (drop.Coins > 0)
            {
                coinChip.Freeze(true);
                coinChip.SetNow(Mathf.Max(0, coinChip.Target - drop.Coins));
            }

            ChestOpening.Play(drop.Tier, drop.Item, drop.Coins, () =>
            {
                if (drop.Coins > 0) RewardFly.Play(RewardFly.Kind.Coins, null, coinChip, drop.Coins);
            });
        }

        /// <summary>A short message above the section bar, then gone. It never moves the shelf, and it
        /// sits where the player is already looking after a tap.</summary>
        private void Say(string message)
        {
            if (toast == null) return;

            toast.text = message;
            toast.BringToFront();
            UiKit.Fade(toast, 1f, ArcadeTheme.TFast);

            int token = ++toastToken;
            toast.schedule.Execute(() =>
            {
                if (token == toastToken) UiKit.Fade(toast, 0f, 0.3f);
            }).StartingIn(2600);
        }

        // ---------- confirm ----------

        /// <summary>
        /// The purchase prompt: what, how much, and a way out. Built once and refilled, rather than
        /// created per purchase, so there is no fresh set of handlers to leak.
        /// </summary>
        private void ShowSheet(int price, string title, string goLabel, Func<VisualElement> art, Action onConfirm)
        {
            pendingBuy = onConfirm;
            sheetArt.Clear();
            sheetArt.Add(art());
            sheetTitle.text = title;
            sheetPrice.text = price.ToString("N0", CultureInfo.InvariantCulture);
            sheetGo.text = goLabel;

            UiFonts.Apply(sheetLayer);
            UiKit.Show(sheetLayer, true);
            sheetLayer.BringToFront();
            sheetLayer.style.opacity = 0f;
            UiKit.Fade(sheetLayer, 1f, ArcadeTheme.TFast);
            GameSfx.PlayUi(UiSound.Open);
        }

        private void ConfirmSheet()
        {
            Action go = pendingBuy;
            CloseSheet();
            go?.Invoke();
        }

        private void CloseSheet()
        {
            pendingBuy = null;
            UiKit.Show(sheetLayer, false);
        }

        // ---------- state ----------

        public void Open()
        {
            if (root == null) return;

            // Removed first, so reopening cannot stack a second handler onto a static event that
            // outlives this panel — the pattern MainMenu and LeagueMenu already use.
            Unsubscribe();
            Wallet.OnChanged += RefreshShelf;
            ShopOffers.OnChanged += RefreshShelf;
            Inventory.OnChanged += RefreshShelf;

            // A chest left mid-opening by a screen change must not leave the balance held.
            coinChip.Freeze(false);
            CloseSheet();
            toast.style.opacity = 0f;
            RefreshShelf();

            UiKit.Show(root, true);
            root.BringToFront();
            root.style.opacity = 0f;
            UiKit.Fade(root, 1f, ArcadeTheme.TFast);
            GameSfx.PlayUi(UiSound.Open);

            // Once the layout has been measured: back to the first section.
            root.schedule.Execute(() =>
            {
                shelf.scrollOffset = Vector2.zero;
                activeSection = -1;
                SelectSection(0);
            }).StartingIn(60);
        }

        public void Close()
        {
            Unsubscribe();
            if (IsOpen) ChestOpening.Cancel();
            if (root != null) UiKit.Show(root, false);
        }

        private void OnDestroy() => Unsubscribe();

        private void Unsubscribe()
        {
            Wallet.OnChanged -= RefreshShelf;
            ShopOffers.OnChanged -= RefreshShelf;
            Inventory.OnChanged -= RefreshShelf;
        }

        private void Back()
        {
            Close();
            OnBack?.Invoke();
        }
    }
}
