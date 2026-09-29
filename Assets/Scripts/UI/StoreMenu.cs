using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using TableFootball.Net;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace TableFootball.UI
{
    /// <summary>
    /// The store: one horizontal shelf of four chests bought with coins, then the daily deal (a free
    /// Common or Rare chest for one ad), then a coin top-up paid for by ads. Skins are no longer sold
    /// one by one. They come out of chests, through <see cref="ChestLoot"/>, which never repeats a
    /// skin the player already owns.
    ///
    /// The store is also where skins are WORN: the Collection button opens the player's owned
    /// cosmetics, tabbed by kind, and tapping one equips it. It used to be the price grid; it is the
    /// same grid with the prices taken off, because every chest drop has to be equippable somewhere.
    ///
    /// Like every other screen it is built in code, reads its data from <c>Net/</c>
    /// (<see cref="ChestLoot"/>, <see cref="ShopOffers"/>, <see cref="Wallet"/>, <see cref="Inventory"/>),
    /// redraws on their change events, and talks out only through <see cref="OnBack"/>. It holds no
    /// economy rules of its own.
    ///
    /// A purchase asks first. One tap spending three thousand coins with no way back would make the
    /// shelf dangerous to browse.
    /// </summary>
    public class StoreMenu : MonoBehaviour
    {
        private GameObject root;
        private CanvasGroup group;

        private TextMeshProUGUI footerLabel;
        private Coroutine statusRoutine;

        // The chest shelf, built once and refreshed in place.
        private readonly MenuButton[] chestButtons = new MenuButton[4];
        private readonly CanvasGroup[] chestPrices = new CanvasGroup[4];

        private MenuButton dealButton;
        private Transform dealArt;
        private TextMeshProUGUI dealTierLabel;
        private TextMeshProUGUI dealName;
        private TextMeshProUGUI dealCountdown;
        private ChestTier shownDealTier = (ChestTier)(-1);

        private MenuButton adButton;
        private TextMeshProUGUI adsLeftLabel;
        private readonly List<Image> adPips = new List<Image>();

        private float nextTick;

        // Buy confirmation.
        private GameObject confirmPanel;
        private Transform confirmArt;
        private TextMeshProUGUI confirmTitle;
        private TextMeshProUGUI confirmPrice;
        private ChestTier? pendingBuy;


        // Collection (the wardrobe).
        private GameObject collectionPanel;
        private RectTransform gridContent;
        private TextMeshProUGUI collectionCaption;
        private CosmeticKind tab = CosmeticKind.FieldSkin;

        /// <summary>Raised when the player backs out.</summary>
        public Action OnBack;

        public bool IsOpen => root != null && root.activeSelf;

        private const string FooterText =
            "Chests never repeat a skin you own. Collected everything? You get coins instead.";

        // The shelf's proportions, in one place. Sized so the whole row fits a 16:9 phone without
        // scrolling; a 4:3 tablet scrolls it sideways.
        private const float CardWidth = 220f;
        private const float DealWidth = 264f;
        private const float CardHeight = 500f;
        private const float ArtHeight = 190f;
        private const float ChestSize = 150f;

        // The collection grid's proportions. Four columns fit the panel with a card wide enough for a
        // legible name.
        private const int Columns = 4;
        private const float GridCardWidth = 232f;
        private const float GridCardHeight = 224f;
        private const float GridGap = 18f;

        public void Build(Transform canvasRoot)
        {
            root = UIFactory.Child(canvasRoot, "StoreMenu");
            UIFactory.Stretch(UIFactory.Rt(root));
            group = root.AddComponent<CanvasGroup>();
            UIFactory.ScrimDim(root.transform);

            UIFactory.ScreenHeader(root.transform, "Store", Back);
            BuildCorners(root.transform);
            BuildShelf(root.transform);
            BuildFooter(root.transform);

            // Last, in this order: each covers the whole screen and must draw over what is under it.
            BuildCollection(root.transform);
            BuildConfirm(root.transform);

            root.SetActive(false);
        }

        // ---------- header corners ----------

        /// <summary>The Collection button top-left and the live balance top-right, level with the title.</summary>
        private void BuildCorners(Transform parent)
        {
            var left = Corner(parent, "CollectionHolder", 0f);
            UIFactory.Button(left.transform, "Collection", MenuButton.Variant.Blue, OpenCollection,
                             ArcadeTheme.HeaderHeight);

            var right = Corner(parent, "Purse", 1f);
            // On a child of the holder, not on the holder: Build reparents its own GameObject to what
            // it is given, and a transform cannot be its own parent.
            UIFactory.Child(right.transform, "Pill").AddComponent<CoinPill>().Build(right.transform);
        }

        private static GameObject Corner(Transform parent, string name, float side)
        {
            var go = UIFactory.Child(parent, name);
            var rt = UIFactory.Rt(go);
            rt.anchorMin = rt.anchorMax = new Vector2(side, 1f);
            rt.pivot = new Vector2(side, 1f);
            rt.sizeDelta = new Vector2(210f, ArcadeTheme.HeaderHeight);
            rt.anchoredPosition = new Vector2(side < 0.5f ? ArcadeTheme.Xl2 : -ArcadeTheme.Xl2, -ArcadeTheme.Xl);

            var v = go.AddComponent<VerticalLayoutGroup>();
            v.childAlignment = TextAnchor.MiddleCenter;
            v.childForceExpandWidth = true;
            v.childForceExpandHeight = false;
            v.childControlWidth = true;
            v.childControlHeight = true;
            return go;
        }

        // ---------- the shelf ----------

        /// <summary>
        /// One horizontal row between the header and the Back button: chests, a divider, the daily
        /// deal, a divider, free coins. A sideways scroll view, centred when it fits, so a narrow
        /// screen scrolls rather than squeezing the cards.
        /// </summary>
        private void BuildShelf(Transform parent)
        {
            var view = UIFactory.Child(parent, "Shelf");
            float top = ArcadeTheme.Xl + ArcadeTheme.HeaderHeight + ArcadeTheme.Lg;
            float bottom = ArcadeTheme.Xl + ArcadeTheme.HeaderHeight + ArcadeTheme.Md;
            UIFactory.Stretch(UIFactory.Rt(view), ArcadeTheme.Xl2, bottom, ArcadeTheme.Xl2, top);
            var scroll = view.AddComponent<ScrollRect>();

            var viewport = UIFactory.Child(view.transform, "Viewport");
            viewport.AddComponent<RectMask2D>();
            UIFactory.Stretch(UIFactory.Rt(viewport), 0);

            var content = UIFactory.Child(viewport.transform, "Content");
            var crt = UIFactory.Rt(content);
            // Centre pivot: ScrollRect keeps content narrower than the viewport at its pivot, so the
            // row sits in the middle of a wide phone and only scrolls when it does not fit.
            crt.anchorMin = new Vector2(0.5f, 0f);
            crt.anchorMax = new Vector2(0.5f, 1f);
            crt.pivot = new Vector2(0.5f, 0.5f);
            crt.sizeDelta = Vector2.zero;

            var h = content.AddComponent<HorizontalLayoutGroup>();
            h.spacing = ArcadeTheme.Xl;
            h.childAlignment = TextAnchor.MiddleCenter;
            h.childForceExpandWidth = false;
            h.childForceExpandHeight = false;
            h.childControlWidth = true;
            h.childControlHeight = true;

            var fitter = content.AddComponent<ContentSizeFitter>();
            fitter.horizontalFit = ContentSizeFitter.FitMode.PreferredSize;
            fitter.verticalFit = ContentSizeFitter.FitMode.Unconstrained;

            scroll.content = crt;
            scroll.viewport = UIFactory.Rt(viewport);
            scroll.horizontal = true;
            scroll.vertical = false;
            scroll.movementType = ScrollRect.MovementType.Elastic;
            scroll.scrollSensitivity = 24f;

            var chests = Section(content.transform, "CHESTS", ArcadeTheme.InkMuted, ArcadeTheme.Lg);
            for (int i = 0; i < 4; i++)
            {
                BuildChestCard(chests, (ChestTier)i);
            }

            Divider(content.transform);
            BuildDealCard(Section(content.transform, "DAILY DEAL", ArcadeTheme.Gold, 0f));

            Divider(content.transform);
            BuildCoinsCard(Section(content.transform, "FREE COINS", ArcadeTheme.InkMuted, 0f));
        }

        /// <summary>A caption over a row of cards. Returns the row to put the cards in.</summary>
        private static Transform Section(Transform parent, string caption, Color captionColor, float gap)
        {
            var col = UIFactory.Child(parent, "Section_" + caption);
            var v = col.AddComponent<VerticalLayoutGroup>();
            v.spacing = ArcadeTheme.Md;
            v.childAlignment = TextAnchor.UpperLeft;
            v.childForceExpandWidth = false;
            v.childForceExpandHeight = false;
            v.childControlWidth = true;
            v.childControlHeight = true;

            var label = UIFactory.Text(col.transform, caption, ArcadeTheme.FsCaption, captionColor,
                                       display: true, bold: true, upper: true, tracking: 4f,
                                       align: TextAlignmentOptions.Left);
            label.gameObject.AddComponent<LayoutElement>().preferredHeight = 22f;

            var row = UIFactory.Child(col.transform, "Row");
            var h = row.AddComponent<HorizontalLayoutGroup>();
            h.spacing = gap;
            h.childAlignment = TextAnchor.UpperLeft;
            h.childForceExpandWidth = false;
            h.childForceExpandHeight = false;
            h.childControlWidth = true;
            h.childControlHeight = true;
            return row.transform;
        }

        private static void Divider(Transform parent)
        {
            var go = UIFactory.Child(parent, "Divider");
            var le = go.AddComponent<LayoutElement>();
            le.preferredWidth = 1f;
            le.minWidth = 1f;
            le.preferredHeight = CardHeight - ArcadeTheme.Xl2;
            var img = go.AddComponent<Image>();
            img.color = ArcadeTheme.Line;
            img.raycastTarget = false;
        }

        /// <summary>
        /// The frame every shelf card shares: an edge in its colour, the panel fill, a soft glow behind
        /// the art, and a column under the art for the words and the button. Returns the column; the
        /// art area comes back through <paramref name="art"/>.
        /// </summary>
        private static Transform CardFrame(Transform parent, string name, float width, Color edge,
                                           Color glow, out Transform art)
        {
            var card = UIFactory.Child(parent, name);
            var le = card.AddComponent<LayoutElement>();
            le.preferredWidth = width;
            le.minWidth = width;
            le.preferredHeight = CardHeight;
            le.minHeight = CardHeight;

            var border = UIFactory.Child(card.transform, "Border");
            UIFactory.RoundedImage(border, ArcadeTheme.RadLg, edge, false);
            UIFactory.Stretch(UIFactory.Rt(border), 0);

            var fill = UIFactory.Child(card.transform, "Fill");
            UIFactory.RoundedImage(fill, ArcadeTheme.RadLg, ArcadeTheme.BgPanel, false);
            UIFactory.Stretch(UIFactory.Rt(fill), 2f);

            var artGo = UIFactory.Child(card.transform, "Art");
            var artRt = UIFactory.Rt(artGo);
            artRt.anchorMin = new Vector2(0f, 1f);
            artRt.anchorMax = new Vector2(1f, 1f);
            artRt.pivot = new Vector2(0.5f, 1f);
            artRt.sizeDelta = new Vector2(0f, ArtHeight);
            artRt.anchoredPosition = Vector2.zero;

            var halo = UIFactory.Child(artGo.transform, "Halo");
            UIFactory.GlowImage(halo, ArcadeTheme.RadLg, 48f, glow);
            var hrt = UIFactory.Rt(halo);
            hrt.anchorMin = hrt.anchorMax = new Vector2(0.5f, 0.45f);
            hrt.pivot = new Vector2(0.5f, 0.5f);
            hrt.sizeDelta = new Vector2(width * 0.85f, ArtHeight * 0.8f);
            art = artGo.transform;

            var col = UIFactory.Child(card.transform, "Column");
            UIFactory.Stretch(UIFactory.Rt(col), ArcadeTheme.Lg, ArcadeTheme.Lg, ArcadeTheme.Lg, ArtHeight);
            var v = col.AddComponent<VerticalLayoutGroup>();
            v.spacing = ArcadeTheme.Sm;
            v.childAlignment = TextAnchor.UpperLeft;
            v.childForceExpandWidth = true;
            v.childForceExpandHeight = false;
            v.childControlWidth = true;
            v.childControlHeight = true;
            return col.transform;
        }

        /// <summary>A small caption pinned into a top corner of a card's art.</summary>
        private static TextMeshProUGUI CornerTag(Transform art, string text, Color color, float side)
        {
            var t = UIFactory.Text(art, text, ArcadeTheme.FsCaption * 0.88f, color, display: true,
                                   bold: true, upper: true, tracking: 3f,
                                   align: side < 0.5f ? TextAlignmentOptions.Left : TextAlignmentOptions.Right);
            var rt = UIFactory.Rt(t.gameObject);
            rt.anchorMin = rt.anchorMax = new Vector2(side, 1f);
            rt.pivot = new Vector2(side, 1f);
            rt.sizeDelta = new Vector2(150f, 24f);
            rt.anchoredPosition = new Vector2(side < 0.5f ? ArcadeTheme.Lg : -ArcadeTheme.Lg, -ArcadeTheme.Md);
            return t;
        }

        private static TextMeshProUGUI CardTitle(Transform col, string text, Color color, float size)
        {
            var t = UIFactory.Text(col, text, size, color, display: true, bold: true, upper: false,
                                   tracking: 1f, align: TextAlignmentOptions.Left);
            t.gameObject.AddComponent<LayoutElement>().preferredHeight = size + 6f;
            return t;
        }

        private static void CardBlurb(Transform col, string text)
        {
            var t = UIFactory.Text(col, text, ArcadeTheme.FsCaption, ArcadeTheme.InkMuted, display: false,
                                   bold: false, upper: false, align: TextAlignmentOptions.TopLeft);
            t.textWrappingMode = TextWrappingModes.Normal;
            t.gameObject.AddComponent<LayoutElement>().preferredHeight = 70f;
        }

        private static void Flex(Transform col)
        {
            UIFactory.Child(col, "Flex").AddComponent<LayoutElement>().flexibleHeight = 1f;
        }

        private void BuildChestCard(Transform parent, ChestTier tier)
        {
            Color tint = UIFactory.ChestColor(tier);
            var col = CardFrame(parent, "Chest_" + tier, CardWidth, tint.WithAlpha(0.55f),
                                tint.WithAlpha(0.26f), out Transform art);

            CornerTag(art, CosmeticCatalog.RarityName((Rarity)(int)tier), tint, 0f);
            PlaceChest(art, tier, ChestSize);

            CardTitle(col, ChestLoot.TierName(tier), ArcadeTheme.Ink, ArcadeTheme.FsBody * 1.1f);
            Flex(col);

            var btn = UIFactory.Button(col, string.Empty, MenuButton.Variant.Blue, () => AskBuy(tier), 56f);
            var price = UIFactory.CoinAmount(btn.transform, ChestLoot.Price(tier).ToString("N0", CultureInfo.InvariantCulture), 34f);
            UIFactory.Stretch(UIFactory.Rt(price), 0);
            var dim = price.AddComponent<CanvasGroup>();
            dim.blocksRaycasts = false;

            chestButtons[(int)tier] = btn;
            chestPrices[(int)tier] = dim;
        }

        private void BuildDealCard(Transform parent)
        {
            // The one card on the screen with a resting gold halo, alongside its gold button.
            var halo = UIFactory.Child(parent, "DealHalo");
            halo.AddComponent<LayoutElement>().ignoreLayout = true;

            var col = CardFrame(parent, "Deal", DealWidth, ArcadeTheme.Gold,
                                ArcadeTheme.Gold.WithAlpha(0.22f), out Transform art);

            UIFactory.GlowImage(halo, ArcadeTheme.RadLg, 30f, ArcadeTheme.Gold.WithAlpha(0.18f));
            var hrt = UIFactory.Rt(halo);
            hrt.anchorMin = hrt.anchorMax = new Vector2(0f, 1f);
            hrt.pivot = new Vector2(0f, 1f);
            hrt.sizeDelta = new Vector2(DealWidth + 28f, CardHeight + 28f);
            hrt.anchoredPosition = new Vector2(-14f, 14f);

            // A gold chip reading FREE in the top-left corner.
            var chip = UIFactory.Child(art, "FreeChip");
            UIFactory.RoundedImage(chip, ArcadeTheme.RadSm, ArcadeTheme.Gold.WithAlpha(0.16f), false);
            var crt = UIFactory.Rt(chip);
            crt.anchorMin = crt.anchorMax = new Vector2(0f, 1f);
            crt.pivot = new Vector2(0f, 1f);
            crt.sizeDelta = new Vector2(72f, 28f);
            crt.anchoredPosition = new Vector2(ArcadeTheme.Md, -ArcadeTheme.Md);
            var free = UIFactory.Text(chip.transform, "FREE", ArcadeTheme.FsCaption * 0.88f, ArcadeTheme.Gold,
                                      display: true, bold: true, upper: true, tracking: 3f);
            UIFactory.Stretch(UIFactory.Rt(free.gameObject), 0);

            dealTierLabel = CornerTag(art, string.Empty, ArcadeTheme.Ink, 1f);

            var holder = UIFactory.Child(art, "ChestHolder");
            UIFactory.Stretch(UIFactory.Rt(holder), 0);
            dealArt = holder.transform;

            dealName = CardTitle(col, string.Empty, ArcadeTheme.Ink, ArcadeTheme.FsBody * 1.1f);
            CardBlurb(col, "Watch one ad to open it. A new deal every day, Common or Rare.");
            Flex(col);

            dealCountdown = UIFactory.Text(col, string.Empty, ArcadeTheme.FsCaption, ArcadeTheme.InkMuted,
                                           display: false, bold: true, upper: false,
                                           align: TextAlignmentOptions.Left, richText: false);
            dealCountdown.gameObject.AddComponent<LayoutElement>().preferredHeight = 24f;

            dealButton = UIFactory.Button(col, "Watch ad", MenuButton.Variant.Primary, ClaimDeal, 56f);
            UIShine.AddTo(dealButton);
        }

        private void BuildCoinsCard(Transform parent)
        {
            var col = CardFrame(parent, "Coins", CardWidth, ArcadeTheme.Coin.WithAlpha(0.45f),
                                ArcadeTheme.Coin.WithAlpha(0.20f), out Transform art);

            // A small pile: two coins behind, one in front.
            PlaceCoin(art, new Vector2(-30f, -18f), 70f);
            PlaceCoin(art, new Vector2(30f, -18f), 70f);
            PlaceCoin(art, new Vector2(0f, 8f), 96f);

            CardTitle(col, "+" + ShopOffers.AdCoins, ArcadeTheme.Coin, ArcadeTheme.FsBody * 1.45f);
            CardTitle(col, "Coins", ArcadeTheme.Ink, ArcadeTheme.FsBody);
            CardBlurb(col, "Watch a short ad for a coin top-up.");
            Flex(col);

            var pipRow = UIFactory.Child(col, "Pips");
            pipRow.AddComponent<LayoutElement>().preferredHeight = 24f;
            var h = pipRow.AddComponent<HorizontalLayoutGroup>();
            h.spacing = ArcadeTheme.Xs + 2f;
            h.childAlignment = TextAnchor.MiddleLeft;
            h.childForceExpandWidth = false;
            h.childForceExpandHeight = false;
            h.childControlWidth = true;
            h.childControlHeight = true;

            for (int i = 0; i < ShopOffers.AdsPerDay; i++)
            {
                var pip = UIFactory.Child(pipRow.transform, "Pip");
                var ple = pip.AddComponent<LayoutElement>();
                ple.preferredWidth = 20f;
                ple.preferredHeight = 8f;
                adPips.Add(UIFactory.RoundedImage(pip, 4, ArcadeTheme.BgRaised, false));
            }

            UIFactory.Child(pipRow.transform, "Gap").AddComponent<LayoutElement>().flexibleWidth = 1f;
            adsLeftLabel = UIFactory.Text(pipRow.transform, string.Empty, ArcadeTheme.FsCaption * 0.9f,
                                          ArcadeTheme.InkMuted, display: false, bold: true,
                                          align: TextAlignmentOptions.Right, richText: false);

            adButton = UIFactory.Button(col, "Watch ad", MenuButton.Variant.Blue, WatchCoinAd, 56f);
        }

        private static void PlaceChest(Transform parent, ChestTier tier, float size)
        {
            var chest = UIFactory.ChestGlyph(parent, tier, size);
            UIFactory.Rt(chest).anchoredPosition = new Vector2(0f, -ArcadeTheme.Sm);
        }

        private static void PlaceCoin(Transform parent, Vector2 pos, float size)
        {
            var coin = UIFactory.CoinGlyph(parent, size);
            UIFactory.Rt(coin).anchoredPosition = pos;
        }

        private void BuildFooter(Transform parent)
        {
            // Along the bottom edge, level with Back, and clear of it.
            footerLabel = UIFactory.Text(parent, FooterText, ArcadeTheme.FsCaption, ArcadeTheme.InkMuted,
                                         display: false, bold: true, upper: false, richText: false);
            var rt = UIFactory.Rt(footerLabel.gameObject);
            rt.anchorMin = new Vector2(0f, 0f);
            rt.anchorMax = new Vector2(1f, 0f);
            rt.pivot = new Vector2(0.5f, 0f);
            rt.offsetMin = new Vector2(ArcadeTheme.Xl2 + ArcadeTheme.BackWidth + ArcadeTheme.Lg, ArcadeTheme.Xl);
            rt.offsetMax = new Vector2(-(ArcadeTheme.Xl2 + ArcadeTheme.BackWidth + ArcadeTheme.Lg),
                                       ArcadeTheme.Xl + ArcadeTheme.HeaderHeight);
        }

        // ---------- refresh ----------

        /// <summary>Repaints the shelf's live state: what can be afforded, and what the ad offers have left.</summary>
        private void RefreshShelf()
        {
            if (root == null) return;

            for (int i = 0; i < chestButtons.Length; i++)
            {
                bool affordable = Wallet.CanAfford(ChestLoot.Price((ChestTier)i));
                chestButtons[i].interactable = affordable;
                chestPrices[i].alpha = affordable ? 1f : 0.45f;
            }

            ChestTier tier = ShopOffers.DealTier;
            if (tier != shownDealTier)
            {
                shownDealTier = tier;
                UIFactory.ClearChildren(dealArt);
                PlaceChest(dealArt, tier, ChestSize + 10f);
                dealTierLabel.text = CosmeticCatalog.RarityName((Rarity)(int)tier);
                dealTierLabel.color = UIFactory.ChestColor(tier);
                dealName.text = ChestLoot.TierName(tier);
            }

            bool dealOpen = !ShopOffers.DealClaimed;
            dealButton.interactable = dealOpen;
            dealButton.label.text = dealOpen ? "Watch ad" : "Claimed today";
            // The screen's one resting glow, and only while there is something to take.
            dealButton.SetAccent(ArcadeTheme.Gold, dealOpen ? 0.5f : 0f);

            int left = ShopOffers.AdsLeft;
            for (int i = 0; i < adPips.Count; i++)
            {
                adPips[i].color = i < left ? ArcadeTheme.Coin : ArcadeTheme.BgRaised;
            }
            adsLeftLabel.text = $"{left} of {ShopOffers.AdsPerDay} left";
            adButton.interactable = left > 0;
            adButton.label.text = left > 0 ? "Watch ad" : "Back tomorrow";

            RefreshCountdown();
        }

        private void RefreshCountdown()
        {
            if (dealCountdown == null) return;
            TimeSpan t = ShopOffers.UntilReset;
            dealCountdown.text = $"New deal in {(int)t.TotalHours:00}:{t.Minutes:00}:{t.Seconds:00}";
        }

        private void Update()
        {
            if (!IsOpen || Time.unscaledTime < nextTick) return;
            nextTick = Time.unscaledTime + 1f;

            // Once a second: the countdown, and — reading the offers is what rolls them over — a
            // screen left open past midnight picks up the new day's deal and ad count.
            bool dealWas = dealButton != null && dealButton.interactable;
            bool dealNow = !ShopOffers.DealClaimed;
            if (dealWas != dealNow || ShopOffers.DealTier != shownDealTier || !adButton.interactable && ShopOffers.AdsLeft > 0)
            {
                RefreshShelf();
                return;
            }

            RefreshCountdown();
        }

        // ---------- actions ----------

        private void AskBuy(ChestTier tier)
        {
            if (!Wallet.CanAfford(ChestLoot.Price(tier)))
            {
                Say("Not enough coins. Play ranked or watch an ad to earn more");
                return;
            }

            ShowConfirm(tier);
        }

        private void Buy(ChestTier tier)
        {
            // Asked and answered in one call: the balance may have moved since the confirm opened.
            if (!Wallet.TrySpend(ChestLoot.Price(tier)))
            {
                Say("Not enough coins. Play ranked or watch an ad to earn more");
                return;
            }

            ShowReveal(ChestLoot.Open(tier));
        }

        private void ClaimDeal()
        {
            ShopOffers.ClaimDeal(drop =>
            {
                if (drop.HasValue) ShowReveal(drop.Value);
                else Say("The ad didn't finish, so the deal is still yours");
            });
        }

        private void WatchCoinAd()
        {
            ShopOffers.WatchCoinAd(paid =>
            {
                Say(paid ? $"+{ShopOffers.AdCoins} coins added" : "The ad didn't finish, so no coins this time");
            });
        }

        /// <summary>
        /// Swaps the footer line for a short message, then puts it back. The footer rather than a popup:
        /// it never moves the shelf, and it sits where the player is already looking after a tap.
        /// </summary>
        private void Say(string message)
        {
            if (footerLabel == null) return;

            footerLabel.text = message;
            footerLabel.color = ArcadeTheme.Coin;

            if (statusRoutine != null) StopCoroutine(statusRoutine);
            if (isActiveAndEnabled) statusRoutine = StartCoroutine(ClearStatus());
        }

        private IEnumerator ClearStatus()
        {
            // Realtime: the front end runs at timeScale 0, and a scaled wait here would never finish.
            yield return new WaitForSecondsRealtime(2.6f);
            ResetFooter();
            statusRoutine = null;
        }

        private void ResetFooter()
        {
            if (footerLabel == null) return;
            footerLabel.text = FooterText;
            footerLabel.color = ArcadeTheme.InkMuted;
        }

        // ---------- confirm ----------

        /// <summary>
        /// The purchase prompt: which chest, how much, and a way out. Built once and refilled, rather
        /// than created per purchase, so there is no fresh set of listeners to leak.
        /// </summary>
        private void BuildConfirm(Transform parent)
        {
            confirmPanel = UIFactory.Child(parent, "ConfirmBuy");
            UIFactory.Stretch(UIFactory.Rt(confirmPanel));
            UIFactory.ScrimDim(confirmPanel.transform, 0.6f);

            var panel = UIFactory.Panel(confirmPanel.transform, "ConfirmPanel");
            var prt = UIFactory.Rt(panel);
            prt.anchorMin = prt.anchorMax = new Vector2(0.5f, 0.5f);
            prt.pivot = new Vector2(0.5f, 0.5f);
            prt.sizeDelta = new Vector2(520f, 420f);

            var col = UIFactory.Child(panel.transform.Find("Fill"), "Col");
            UIFactory.Stretch(UIFactory.Rt(col), 26f);

            var v = col.AddComponent<VerticalLayoutGroup>();
            v.spacing = ArcadeTheme.Md;
            v.childAlignment = TextAnchor.UpperCenter;
            v.childForceExpandWidth = true;
            v.childForceExpandHeight = false;
            v.childControlWidth = true;
            v.childControlHeight = true;

            var art = UIFactory.Child(col.transform, "Art");
            art.AddComponent<LayoutElement>().preferredHeight = 130f;
            confirmArt = art.transform;

            confirmTitle = UIFactory.Text(col.transform, string.Empty, ArcadeTheme.FsBody * 1.2f,
                                          ArcadeTheme.Ink, display: true, bold: true, upper: false,
                                          tracking: 1f, richText: false);
            confirmTitle.gameObject.AddComponent<LayoutElement>().preferredHeight = 36f;

            var priceRow = UIFactory.CoinAmount(col.transform, "0", 40f);
            confirmPrice = priceRow.GetComponentInChildren<TextMeshProUGUI>();

            UIFactory.Spacer(col.transform, 4f);

            var buttons = UIFactory.Child(col.transform, "Buttons");
            buttons.AddComponent<LayoutElement>().preferredHeight = 62f;
            var h = buttons.AddComponent<HorizontalLayoutGroup>();
            h.spacing = ArcadeTheme.Sm;
            h.childForceExpandWidth = true;
            h.childForceExpandHeight = true;
            h.childControlWidth = true;
            h.childControlHeight = true;

            UIFactory.Button(buttons.transform, "Cancel", MenuButton.Variant.Ghost, CancelBuy);
            UIFactory.Button(buttons.transform, "Open", MenuButton.Variant.Primary, ConfirmBuy);

            confirmPanel.SetActive(false);
        }

        private void ShowConfirm(ChestTier tier)
        {
            pendingBuy = tier;
            if (confirmPanel == null) return;

            UIFactory.ClearChildren(confirmArt);
            UIFactory.ChestGlyph(confirmArt, tier, 130f);
            confirmTitle.text = $"Open {ChestLoot.TierName(tier)}?";
            confirmPrice.text = ChestLoot.Price(tier).ToString("N0", CultureInfo.InvariantCulture);
            confirmPanel.SetActive(true);
            confirmPanel.transform.SetAsLastSibling();
        }

        private void ConfirmBuy()
        {
            ChestTier? tier = pendingBuy;
            CancelBuy();
            if (tier.HasValue) Buy(tier.Value);
        }

        private void CancelBuy()
        {
            pendingBuy = null;
            if (confirmPanel != null) confirmPanel.SetActive(false);
        }

        // ---------- reveal ----------

        /// <summary>
        /// Plays the chest opening over the store for a chest that has already been rolled and
        /// granted. The store is uGUI and the opening is UI Toolkit, and which of the two draws on
        /// top is not something either system promises — so the store fades out and stops taking
        /// touches while it plays, and comes back when the player collects.
        /// </summary>
        private void ShowReveal(ChestDrop drop)
        {
            group.alpha = 0f;
            group.blocksRaycasts = false;

            ChestOpening.Play(drop.Tier, drop.Item, drop.Coins, () =>
            {
                if (!IsOpen) return;
                group.alpha = 1f;
                group.blocksRaycasts = true;
            });
        }

        // ---------- collection ----------

        /// <summary>
        /// The wardrobe: every cosmetic the player owns, tabbed by kind, tap to wear. League badges
        /// are shown even when locked, because a wardrobe that only listed what you had won would
        /// never tell a player what climbing the ladder is FOR.
        /// </summary>
        private void BuildCollection(Transform parent)
        {
            collectionPanel = UIFactory.Child(parent, "Collection");
            UIFactory.Stretch(UIFactory.Rt(collectionPanel));
            UIFactory.ScrimDim(collectionPanel.transform, 0.9f);

            UIFactory.ScreenHeader(collectionPanel.transform, "Collection", CloseCollection);

            var panel = UIFactory.Panel(collectionPanel.transform, "CollectionPanel");
            var prt = UIFactory.Rt(panel);
            prt.anchorMin = prt.anchorMax = new Vector2(0.5f, 0.5f);
            prt.pivot = new Vector2(0.5f, 0.5f);
            // Clears the shared header even on a 20:9 phone, where the canvas is only ~805 tall.
            prt.sizeDelta = new Vector2(1050f, 600f);
            prt.anchoredPosition = new Vector2(0f, -10f);

            // Laid-out content goes in a stretched column on top of the panel's frame, so a layout
            // group never touches the shadow, border or fill. Same split as LeagueMenu.
            var content = UIFactory.Child(panel.transform, "Content");
            UIFactory.Stretch(UIFactory.Rt(content), 0f);

            var col = content.AddComponent<VerticalLayoutGroup>();
            col.padding = new RectOffset(24, 24, 24, 24);
            col.spacing = ArcadeTheme.Md;
            col.childAlignment = TextAnchor.UpperCenter;
            col.childForceExpandWidth = true;
            col.childForceExpandHeight = false;
            col.childControlWidth = true;
            col.childControlHeight = true;

            var labels = new string[CosmeticCatalog.TabKinds.Length];
            for (int i = 0; i < labels.Length; i++)
            {
                labels[i] = CosmeticCatalog.KindName(CosmeticCatalog.TabKinds[i]);
            }

            UIFactory.Segmented(content.transform, labels, 0, index =>
            {
                tab = CosmeticCatalog.TabKinds[Mathf.Clamp(index, 0, CosmeticCatalog.TabKinds.Length - 1)];
                RedrawCollection();
            }, 48f);

            collectionCaption = UIFactory.Text(content.transform, string.Empty, ArcadeTheme.FsCaption,
                                               ArcadeTheme.InkMuted, display: false, bold: true,
                                               upper: true, tracking: 3f, richText: false);
            collectionCaption.gameObject.AddComponent<LayoutElement>().preferredHeight = 22f;

            var holder = UIFactory.Child(content.transform, "GridHolder");
            var le = holder.AddComponent<LayoutElement>();
            le.preferredHeight = 420f;
            le.flexibleHeight = 1f;
            le.minHeight = 160f;
            gridContent = UIFactory.ScrollList(holder.transform, GridGap);

            collectionPanel.SetActive(false);
        }

        private void OpenCollection()
        {
            if (collectionPanel == null) return;
            collectionPanel.SetActive(true);
            collectionPanel.transform.SetAsLastSibling();
            RedrawCollection();
        }

        private void CloseCollection()
        {
            if (collectionPanel != null) collectionPanel.SetActive(false);
        }

        private void RedrawCollection()
        {
            if (gridContent == null || collectionPanel == null || !collectionPanel.activeSelf)
            {
                return;
            }

            UIFactory.ClearChildren(gridContent);

            bool badges = tab == CosmeticKind.Badge;
            collectionCaption.text = badges ? "League badges are earned in ranked" : "Tap a skin to wear it";

            List<CosmeticItem> items = CosmeticCatalog.OfKind(tab);
            Transform row = null;

            for (int i = 0, shown = 0; i < items.Count; i++)
            {
                // What you own (the catalogue default counts: everybody owns it, and taking a skin OFF
                // means putting it back on), plus every badge. Unowned skins come from chests.
                CosmeticItem item = items[i];
                if (!badges && !item.IsDefault && !Inventory.Owns(item.Id)) continue;

                if (shown % Columns == 0) row = NewRow();
                AddCard(row, item);
                shown++;
            }
        }

        private Transform NewRow()
        {
            var row = UIFactory.Child(gridContent, "Row");
            row.AddComponent<LayoutElement>().preferredHeight = GridCardHeight;

            var h = row.AddComponent<HorizontalLayoutGroup>();
            h.spacing = GridGap;
            // Left, not centre: a final row holding two cards should line up under the four above it.
            h.childAlignment = TextAnchor.MiddleLeft;
            h.childForceExpandWidth = false;
            h.childForceExpandHeight = false;
            h.childControlWidth = true;
            h.childControlHeight = true;

            return row.transform;
        }

        /// <summary>
        /// One item card: placeholder art, name, rarity, and its ownership state. Built on
        /// <see cref="MenuButton"/> so the whole card hovers, presses and clicks like a button, with
        /// its edge in the item's rarity colour.
        /// </summary>
        private void AddCard(Transform parent, CosmeticItem item)
        {
            bool owned = Inventory.Owns(item.Id);
            bool equipped = Inventory.IsEquipped(item.Id);
            Color rarity = UIFactory.RarityColor(item.Rarity);

            var card = UIFactory.Child(parent, "Card_" + item.Id);
            var cle = card.AddComponent<LayoutElement>();
            cle.preferredWidth = GridCardWidth;
            cle.minWidth = GridCardWidth;
            cle.preferredHeight = GridCardHeight;

            var glowGo = UIFactory.Child(card.transform, "Glow");
            var glow = UIFactory.GlowImage(glowGo, ArcadeTheme.RadMd, 26f, rarity.WithAlpha(0f));
            UIFactory.Stretch(UIFactory.Rt(glowGo), -14f);

            var borderGo = UIFactory.Child(card.transform, "Border");
            var border = UIFactory.RoundedImage(borderGo, ArcadeTheme.RadMd, rarity, false);
            UIFactory.Stretch(UIFactory.Rt(borderGo), 0);

            var fillGo = UIFactory.Child(card.transform, "Fill");
            var fill = UIFactory.RoundedImage(fillGo, ArcadeTheme.RadMd, ArcadeTheme.BgRaised, true);
            UIFactory.Stretch(UIFactory.Rt(fillGo), 2f);

            var btn = card.AddComponent<MenuButton>();
            btn.fill = fill;
            btn.border = border;
            btn.glow = glow;
            btn.label = null;
            btn.targetGraphic = fill;
            btn.Configure(MenuButton.Variant.Neutral);
            // Equipped is the one card per tab that lights up at rest.
            btn.SetAccent(rarity, equipped ? 0.55f : 0f);
            btn.onClick.AddListener(() => Tapped(item));

            var body = UIFactory.Child(card.transform, "Body");
            UIFactory.Stretch(UIFactory.Rt(body), 8f);

            var v = body.AddComponent<VerticalLayoutGroup>();
            v.spacing = ArcadeTheme.Xs;
            v.childAlignment = TextAnchor.UpperCenter;
            v.childForceExpandWidth = true;
            v.childForceExpandHeight = false;
            v.childControlWidth = true;
            v.childControlHeight = true;

            var picture = UIFactory.Child(body.transform, "Picture");
            picture.AddComponent<LayoutElement>().preferredHeight = 112f;

            if (item.Kind == CosmeticKind.Badge)
            {
                UIFactory.LeagueBadgeGlyph(picture.transform, LeagueBadges.LeagueOf(item.Id), 96f);

                if (!owned)
                {
                    var locked = picture.AddComponent<CanvasGroup>();
                    locked.alpha = 0.28f;
                    locked.blocksRaycasts = false;
                }
            }
            else
            {
                UIFactory.CosmeticSwatch(picture.transform, item);
            }

            var name = UIFactory.Text(body.transform, item.Name, ArcadeTheme.FsCaption, ArcadeTheme.Ink,
                                      display: true, bold: true, upper: false, tracking: 1f,
                                      richText: false);
            name.gameObject.AddComponent<LayoutElement>().preferredHeight = 24f;
            name.overflowMode = TextOverflowModes.Ellipsis;

            var rar = UIFactory.Text(body.transform, CosmeticCatalog.RarityName(item.Rarity),
                                     ArcadeTheme.FsCaption * 0.78f, rarity, display: false, bold: true,
                                     upper: true, tracking: 3f);
            rar.gameObject.AddComponent<LayoutElement>().preferredHeight = 18f;

            AddCardFooter(body.transform, item, owned, equipped);

            if (equipped)
            {
                var check = UIFactory.EquippedCheck(card.transform, 30f);
                var checkRt = UIFactory.Rt(check);
                checkRt.anchorMin = checkRt.anchorMax = new Vector2(1f, 1f);
                checkRt.pivot = new Vector2(1f, 1f);
                checkRt.anchoredPosition = new Vector2(-8f, -8f);
            }
        }

        /// <summary>The bottom line of a card: EQUIPPED, tap to equip, or which league earns a badge.</summary>
        private static void AddCardFooter(Transform parent, CosmeticItem item, bool owned, bool equipped)
        {
            var footer = UIFactory.Child(parent, "Footer");
            footer.AddComponent<LayoutElement>().preferredHeight = 34f;

            TextMeshProUGUI t;
            if (equipped)
            {
                UIFactory.RoundedImage(footer, ArcadeTheme.RadSm,
                                       UIFactory.RarityColor(item.Rarity).WithAlpha(0.22f), false);
                t = UIFactory.Text(footer.transform, "EQUIPPED", ArcadeTheme.FsCaption * 0.82f,
                                   UIFactory.RarityColor(item.Rarity), display: true, bold: true,
                                   upper: true, tracking: 3f);
            }
            else if (owned)
            {
                t = UIFactory.Text(footer.transform, "Tap to equip", ArcadeTheme.FsCaption * 0.82f,
                                   ArcadeTheme.InkMuted, display: false, bold: true, upper: true,
                                   tracking: 3f);
            }
            else
            {
                League league = LeagueBadges.LeagueOf(item.Id);
                t = UIFactory.Text(footer.transform, $"Reach {Leagues.Name(league)}",
                                   ArcadeTheme.FsCaption * 0.82f, UIFactory.LeagueColor(league),
                                   display: false, bold: true, upper: true, tracking: 3f,
                                   richText: false);
            }

            UIFactory.Stretch(UIFactory.Rt(t.gameObject), 0);
        }

        private void Tapped(CosmeticItem item)
        {
            bool badge = item.Kind == CosmeticKind.Badge;

            if (!Inventory.Owns(item.Id))
            {
                if (badge)
                {
                    collectionCaption.text = $"Reach {Leagues.Name(LeagueBadges.LeagueOf(item.Id))} league to earn this badge";
                }
                return;
            }

            if (Inventory.IsEquipped(item.Id))
            {
                // A badge is the one cosmetic with no default underneath it, so it is the only one that
                // can genuinely be taken OFF.
                if (badge)
                {
                    Inventory.Unequip(CosmeticKind.Badge);
                    collectionCaption.text = "Badge removed";
                }
                return;
            }

            Inventory.Equip(item.Id);
            collectionCaption.text = badge ? $"{item.Name} now shows beside your name" : $"{item.Name} equipped";
        }

        // ---------- state ----------

        public void Open()
        {
            if (root == null) return;

            // Removed first, so reopening cannot stack a second handler onto a static event that
            // outlives this panel — the pattern MainMenu and LeagueMenu already use.
            Wallet.OnChanged -= RefreshShelf;
            Wallet.OnChanged += RefreshShelf;
            ShopOffers.OnChanged -= RefreshShelf;
            ShopOffers.OnChanged += RefreshShelf;
            Inventory.OnChanged -= RedrawCollection;
            Inventory.OnChanged += RedrawCollection;

            CancelBuy();
            CloseCollection();
            ResetFooter();

            root.SetActive(true);
            root.transform.SetAsLastSibling();
            group.alpha = 1f;
            group.blocksRaycasts = true;

            RefreshShelf();
        }

        public void Close()
        {
            Unsubscribe();
            // Guarded: GameFlow closes screens that are not open, and that must not cancel a chest
            // playing over another one.
            if (IsOpen) ChestOpening.Cancel();

            if (root != null)
            {
                group.blocksRaycasts = false;
                root.SetActive(false);
            }
        }

        private void OnDestroy()
        {
            Unsubscribe();
        }

        private void Unsubscribe()
        {
            Wallet.OnChanged -= RefreshShelf;
            ShopOffers.OnChanged -= RefreshShelf;
            Inventory.OnChanged -= RedrawCollection;
        }

        private void Back()
        {
            Close();
            OnBack?.Invoke();
        }
    }
}
