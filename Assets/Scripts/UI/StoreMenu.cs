using System;
using System.Collections;
using System.Collections.Generic;
using TableFootball.Net;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace TableFootball.UI
{
    /// <summary>
    /// The store: four tabs of cosmetics, bought with the gold coins earned from ranked weeks and the
    /// level path.
    ///
    /// PLACEHOLDER ARTWORK, real everything else. Each card draws a
    /// <see cref="UIFactory.CosmeticSwatch"/> rather than a skin, because the skins do not exist yet —
    /// but the prices, the balance, the purchase, the ownership and the equipping are all live, so the
    /// screen can be played and tuned now and the swatch is the single thing that gets replaced.
    ///
    /// Like every other screen it is built in code, reads its data from <c>Net/</c>
    /// (<see cref="CosmeticCatalog"/>, <see cref="Inventory"/>, <see cref="Wallet"/>), redraws on
    /// their change events, and talks out only through <see cref="OnBack"/>. It holds no economy rules
    /// of its own: what an item costs and whether it can be afforded are questions for the wallet.
    ///
    /// A purchase asks first. One tap spending three thousand coins with no way back would make the
    /// grid dangerous to browse, and browsing is most of what a store is for.
    /// </summary>
    public class StoreMenu : MonoBehaviour
    {
        private GameObject root;
        private CanvasGroup group;

        private RectTransform gridContent;
        private TextMeshProUGUI headerCaption;
        private TextMeshProUGUI statusLabel;
        private GameObject confirmPanel;
        private TextMeshProUGUI confirmTitle;
        private TextMeshProUGUI confirmPrice;
        private CosmeticItem pendingBuy;

        private CosmeticKind tab = CosmeticKind.FieldSkin;
        private Coroutine statusRoutine;

        /// <summary>Raised when the player backs out.</summary>
        public Action OnBack;

        public bool IsOpen => root != null && root.activeSelf;

        // The grid's proportions, in one place. Four columns is what the panel's width divides into
        // with a card wide enough for a legible name — three left dead space, five made the names wrap.
        private const int Columns = 4;
        private const float CardWidth = 232f;
        private const float CardHeight = 224f;
        private const float CardGap = 18f;

        public void Build(Transform canvasRoot)
        {
            root = UIFactory.Child(canvasRoot, "StoreMenu");
            UIFactory.Stretch(UIFactory.Rt(root));
            group = root.AddComponent<CanvasGroup>();
            UIFactory.ScrimDim(root.transform);

            UIFactory.ScreenHeader(root.transform, "Store", Back);

            var panel = UIFactory.Panel(root.transform, "StorePanel");
            var prt = UIFactory.Rt(panel);
            prt.anchorMin = prt.anchorMax = new Vector2(0.5f, 0.5f);
            prt.pivot = new Vector2(0.5f, 0.5f);
            // Clears the shared header even on a 20:9 phone, where the canvas is only ~805 tall.
            prt.sizeDelta = new Vector2(1050f, 680f);
            prt.anchoredPosition = new Vector2(0f, -40f);

            // The panel's own children are its frame; laid-out content goes in a stretched column on
            // top, so a layout group never touches the shadow, border or fill. Same split as
            // LeagueMenu.
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

            BuildHeader(content.transform);
            BuildTabs(content.transform);
            BuildStatus(content.transform);
            BuildGrid(content.transform);

            // Last inside the ROOT, not the column: it covers the whole screen and must draw over the
            // panel it is asking about.
            BuildConfirm(root.transform);

            root.SetActive(false);
        }

        /// <summary>
        /// The section title on the left and the live balance on the right — the two things a shopper
        /// needs before they look at a single price.
        /// </summary>
        private void BuildHeader(Transform parent)
        {
            var header = UIFactory.Child(parent, "Header");
            // Was 48 — a thin strip the coin pill (a full bordered slab) had to squeeze into beside
            // the plain caption text, which is what made it read as pinned on rather than part of one
            // header. Taller gives both room to sit on the same visual weight.
            header.AddComponent<LayoutElement>().preferredHeight = 60f;

            var h = header.AddComponent<HorizontalLayoutGroup>();
            h.spacing = ArcadeTheme.Md;
            h.childAlignment = TextAnchor.MiddleLeft;
            h.childForceExpandWidth = false;
            h.childForceExpandHeight = true;
            h.childControlWidth = true;
            h.childControlHeight = true;

            headerCaption = UIFactory.Text(header.transform, "COSMETICS", ArcadeTheme.FsCaption,
                                           ArcadeTheme.InkMuted, display: false, bold: true,
                                           upper: true, tracking: 4f,
                                           align: TextAlignmentOptions.Left);
            headerCaption.gameObject.AddComponent<LayoutElement>().flexibleWidth = 1f;

            var purse = UIFactory.Child(header.transform, "Purse");
            var ple = purse.AddComponent<LayoutElement>();
            ple.preferredWidth = 210f;
            ple.minWidth = 210f;
            // On a child of the cell, not on the cell — Build reparents its own GameObject to what it
            // is given, and a transform cannot be its own parent.
            UIFactory.Child(purse.transform, "Pill").AddComponent<CoinPill>().Build(purse.transform);
        }

        private void BuildTabs(Transform parent)
        {
            var labels = new string[CosmeticCatalog.TabKinds.Length];
            for (int i = 0; i < labels.Length; i++)
            {
                labels[i] = CosmeticCatalog.KindName(CosmeticCatalog.TabKinds[i]);
            }

            UIFactory.Segmented(parent, labels, 0, index =>
            {
                tab = CosmeticCatalog.TabKinds[Mathf.Clamp(index, 0, CosmeticCatalog.TabKinds.Length - 1)];
                Redraw();
            }, 48f);
        }

        /// <summary>
        /// One line under the tabs for the store to answer back — bought, equipped, or not enough
        /// coins. A reserved row rather than a popup: it never moves the grid, and a message that
        /// appears where the player is already looking is read, where one in a corner is not.
        /// </summary>
        private void BuildStatus(Transform parent)
        {
            statusLabel = UIFactory.Text(parent, string.Empty, ArcadeTheme.FsCaption, ArcadeTheme.Coin,
                                         display: false, bold: true, upper: true, tracking: 3f,
                                         richText: false);
            statusLabel.gameObject.AddComponent<LayoutElement>().preferredHeight = 22f;
        }

        private void BuildGrid(Transform parent)
        {
            var holder = UIFactory.Child(parent, "GridHolder");
            var le = holder.AddComponent<LayoutElement>();
            le.preferredHeight = 470f;
            le.flexibleHeight = 1f;
            // The one element allowed to give up height when the panel is short, never below a
            // scrollable minimum — so Back keeps the room it asked for instead of drifting into the
            // grid. Same reasoning as LeagueMenu's list.
            le.minHeight = 160f;

            gridContent = UIFactory.ScrollList(holder.transform, CardGap);
        }

        // ---------- the grid ----------

        private void Redraw()
        {
            if (gridContent == null)
            {
                return;
            }

            UIFactory.ClearChildren(gridContent);

            bool badges = tab == CosmeticKind.Badge;
            if (headerCaption != null)
            {
                // Says what the tab IS rather than what the screen is called. A shelf of badges under a
                // heading reading "cosmetics" in a screen titled "store" invites exactly one question,
                // and this answers it before it is asked.
                headerCaption.text = badges ? "LEAGUE BADGES — EARNED, NOT SOLD" : "COSMETICS";
            }

            List<CosmeticItem> items = CosmeticCatalog.OfKind(tab);
            Transform row = null;

            for (int i = 0, shown = 0; i < items.Count; i++)
            {
                // Three things belong on a tab: what is for sale, the catalogue default (owned by
                // everybody, never priced — but it has to be reachable, because taking a bought skin
                // OFF means putting the default back on), and the badges, which are the wardrobe.
                // Anything else in the catalogue is not shopping.
                CosmeticItem item = items[i];
                if (!item.IsForSale && !item.IsDefault && item.Kind != CosmeticKind.Badge) continue;

                if (shown % Columns == 0) row = NewRow();
                AddCard(row, item);
                shown++;
            }
        }

        private Transform NewRow()
        {
            var row = UIFactory.Child(gridContent, "Row");
            row.AddComponent<LayoutElement>().preferredHeight = CardHeight;

            var h = row.AddComponent<HorizontalLayoutGroup>();
            h.spacing = CardGap;
            // Left, not centre: a final row holding two cards should line up under the four above it,
            // not float in the middle of the grid.
            h.childAlignment = TextAnchor.MiddleLeft;
            h.childForceExpandWidth = false;
            h.childForceExpandHeight = false;
            h.childControlWidth = true;
            h.childControlHeight = true;

            return row.transform;
        }

        /// <summary>
        /// One item card: placeholder art, name, rarity, and a price or its ownership state.
        ///
        /// Built on <see cref="MenuButton"/> like every other control, so the whole card hovers,
        /// presses and clicks exactly as a button does — and then <see cref="MenuButton.SetAccent"/>
        /// paints its edge in the item's rarity colour, which is the one thing that makes a wall of
        /// cards scannable.
        /// </summary>
        private void AddCard(Transform parent, CosmeticItem item)
        {
            bool owned = Inventory.Owns(item.Id);
            bool equipped = Inventory.IsEquipped(item.Id);
            Color rarity = UIFactory.RarityColor(item.Rarity);

            var card = UIFactory.Child(parent, "Card_" + item.Id);
            var cle = card.AddComponent<LayoutElement>();
            cle.preferredWidth = CardWidth;
            cle.minWidth = CardWidth;
            cle.preferredHeight = CardHeight;

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
            // Equipped is the one card per tab that lights up at rest — the wardrobe's answer to
            // "which of these am I wearing", readable without reading a word of it.
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
                // A badge draws itself — it is the same medal that sits beside the player's name, at
                // card size. A rarity swatch here would be a picture of a badge rather than the badge.
                UIFactory.LeagueBadgeGlyph(picture.transform, LeagueBadges.LeagueOf(item.Id), 96f);

                // Locked badges are shown, not hidden. A wardrobe that only listed what you had won
                // would never tell a player what climbing the ladder is FOR.
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

            // The player's own catalogue text, so rich text stays off — a stray tag in a name would
            // rewrite the card's layout.
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

            // A clear checkmark badge, not just a footer word — pinned over the top-right corner so
            // "this is what you're wearing" reads before anything else on the card does. Added last
            // (on the card itself, not inside the inset body) so it draws in front and sits proud of
            // the card's own edge.
            if (equipped)
            {
                var check = UIFactory.EquippedCheck(card.transform, 30f);
                var checkRt = UIFactory.Rt(check);
                checkRt.anchorMin = checkRt.anchorMax = new Vector2(1f, 1f);
                checkRt.pivot = new Vector2(1f, 1f);
                checkRt.anchoredPosition = new Vector2(-8f, -8f);
            }
        }

        /// <summary>
        /// The bottom line of a card, which is the whole state machine of a store item in one row:
        /// a price when it can be bought, EQUIPPED when it is being worn, OWNED when it is not.
        /// </summary>
        private static void AddCardFooter(Transform parent, CosmeticItem item, bool owned, bool equipped)
        {
            var footer = UIFactory.Child(parent, "Footer");
            footer.AddComponent<LayoutElement>().preferredHeight = 34f;

            if (equipped)
            {
                UIFactory.RoundedImage(footer, ArcadeTheme.RadSm,
                                       UIFactory.RarityColor(item.Rarity).WithAlpha(0.22f), false);
                var t = UIFactory.Text(footer.transform, "EQUIPPED", ArcadeTheme.FsCaption * 0.82f,
                                       UIFactory.RarityColor(item.Rarity), display: true, bold: true,
                                       upper: true, tracking: 3f);
                UIFactory.Stretch(UIFactory.Rt(t.gameObject), 0);
                return;
            }

            if (owned)
            {
                var t = UIFactory.Text(footer.transform, "Tap to equip", ArcadeTheme.FsCaption * 0.82f,
                                       ArcadeTheme.InkMuted, display: false, bold: true, upper: true,
                                       tracking: 3f);
                UIFactory.Stretch(UIFactory.Rt(t.gameObject), 0);
                return;
            }

            // A badge has no price to show, because there is no price at which it can be had — it says
            // which league to reach instead, which is the only way to get one.
            if (item.Kind == CosmeticKind.Badge)
            {
                League league = LeagueBadges.LeagueOf(item.Id);
                var t = UIFactory.Text(footer.transform, $"Reach {Leagues.Name(league)}",
                                       ArcadeTheme.FsCaption * 0.82f, UIFactory.LeagueColor(league),
                                       display: false, bold: true, upper: true, tracking: 3f,
                                       richText: false);
                UIFactory.Stretch(UIFactory.Rt(t.gameObject), 0);
                return;
            }

            // Dimmed when it cannot be afforded, so the wall of prices sorts itself into "today" and
            // "later" without the player doing arithmetic against the balance in the header.
            bool affordable = Wallet.CanAfford(item.Price);
            var priceRow = UIFactory.CoinAmount(footer.transform, item.Price.ToString(), 30f,
                                                affordable ? ArcadeTheme.Coin : ArcadeTheme.InkMuted);
            UIFactory.Stretch(UIFactory.Rt(priceRow), 0);
            if (!affordable)
            {
                var dim = priceRow.AddComponent<CanvasGroup>();
                dim.alpha = 0.55f;
                dim.blocksRaycasts = false;
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
                    // A badge is the one cosmetic with no default underneath it, so it is also the only
                    // one that can genuinely be taken OFF — tapping the worn badge removes it. Every
                    // other kind always has something equipped, and "unequip" there would mean nothing.
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
                League league = LeagueBadges.LeagueOf(item.Id);
                Say($"Reach {Leagues.Name(league)} league to earn this badge");
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
            // Worn straight away. Nobody buys a skin to leave it in a drawer, and the alternative is
            // a purchase that visibly changes nothing.
            Inventory.Equip(item.Id);
            Say($"{item.Name} unlocked and equipped");
        }

        private void Say(string message)
        {
            if (statusLabel == null)
            {
                return;
            }

            statusLabel.text = message;

            if (statusRoutine != null) StopCoroutine(statusRoutine);
            statusRoutine = StartCoroutine(ClearStatus());
        }

        private IEnumerator ClearStatus()
        {
            // Realtime: the front end runs at timeScale 0, and a scaled wait here would never finish.
            yield return new WaitForSecondsRealtime(2.6f);
            if (statusLabel != null) statusLabel.text = string.Empty;
            statusRoutine = null;
        }

        // ---------- confirm ----------

        /// <summary>
        /// The purchase prompt: what, how much, and a way out. Built once and shown or hidden, rather
        /// than created per purchase — it is the same three widgets every time, and rebuilding it
        /// would be a fresh set of listeners to leak.
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
            prt.sizeDelta = new Vector2(520f, 300f);

            var fill = panel.transform.Find("Fill");
            var col = UIFactory.Child(fill, "Col");
            UIFactory.Stretch(UIFactory.Rt(col), 26f);

            var v = col.AddComponent<VerticalLayoutGroup>();
            v.spacing = ArcadeTheme.Md;
            v.childAlignment = TextAnchor.UpperCenter;
            v.childForceExpandWidth = true;
            v.childForceExpandHeight = false;
            v.childControlWidth = true;
            v.childControlHeight = true;

            confirmTitle = UIFactory.Text(col.transform, "Buy this?", ArcadeTheme.FsBody,
                                          ArcadeTheme.Ink, display: true, bold: true, upper: false,
                                          tracking: 1f, richText: false);
            confirmTitle.gameObject.AddComponent<LayoutElement>().preferredHeight = 34f;

            var priceRow = UIFactory.CoinAmount(col.transform, "0", 44f);
            confirmPrice = priceRow.GetComponentInChildren<TextMeshProUGUI>();

            UIFactory.Spacer(col.transform, 6f);

            var buttons = UIFactory.Child(col.transform, "Buttons");
            buttons.AddComponent<LayoutElement>().preferredHeight = 62f;
            var h = buttons.AddComponent<HorizontalLayoutGroup>();
            h.spacing = ArcadeTheme.Sm;
            h.childForceExpandWidth = true;
            h.childForceExpandHeight = true;
            h.childControlWidth = true;
            h.childControlHeight = true;

            UIFactory.Button(buttons.transform, "Buy", MenuButton.Variant.Primary, ConfirmBuy);
            UIFactory.Button(buttons.transform, "Cancel", MenuButton.Variant.Ghost, CancelBuy);

            confirmPanel.SetActive(false);
        }

        private void ShowConfirm(CosmeticItem item)
        {
            pendingBuy = item;
            if (confirmPanel == null) return;

            confirmTitle.text = $"Buy {item.Name}?";
            confirmPrice.text = item.Price.ToString();
            confirmPanel.SetActive(true);
            confirmPanel.transform.SetAsLastSibling();
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
            if (confirmPanel != null) confirmPanel.SetActive(false);
        }

        // ---------- state ----------

        public void Open()
        {
            if (root == null) return;

            // Removed first, so reopening cannot stack a second handler onto a static event that
            // outlives this panel — the pattern MainMenu and LeagueMenu already use.
            Wallet.OnChanged -= Redraw;
            Wallet.OnChanged += Redraw;
            Inventory.OnChanged -= Redraw;
            Inventory.OnChanged += Redraw;

            CancelBuy();
            if (statusLabel != null) statusLabel.text = string.Empty;

            root.SetActive(true);
            root.transform.SetAsLastSibling();
            group.blocksRaycasts = true;

            Redraw();
        }

        public void Close()
        {
            Wallet.OnChanged -= Redraw;
            Inventory.OnChanged -= Redraw;

            if (root != null)
            {
                group.blocksRaycasts = false;
                root.SetActive(false);
            }
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
