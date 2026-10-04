using System;
using System.Collections.Generic;
using TableFootball.Net;
using TableFootball.UI.Toolkit;
using UnityEngine;
using UnityEngine.UIElements;

namespace TableFootball.UI
{
    /// <summary>
    /// The Level Road: every level from 2 to <see cref="PlayerProgress.MaxLevel"/> as a stop on a road
    /// that runs sideways, with the reward it pays. Coin levels are small cards; the milestones — Lucky
    /// Chests and named skins — are big ones with a real picture, so the prizes worth walking to can be
    /// seen from a long way off. Built on UI Toolkit, styled by <c>Resources/UI/Styles/LevelRoad.uss</c>.
    ///
    /// The road is drawn by the columns themselves: each carries a stretch of rail through its marker,
    /// lit cyan as far as the player has walked, so the road is continuous by construction rather than
    /// by a separate line measured to match.
    ///
    /// Columns are built ONCE and refreshed in place. Claiming rebuilds nothing — the card that changed
    /// repaints and the rest sit still — because rebuilding sixty columns on every tap would throw the
    /// player back to the start of the road each time they collected something.
    ///
    /// It reads its data from <c>Net/</c> (<see cref="PlayerProgress"/> for the level,
    /// <see cref="LevelPath"/> for the rewards and claims), redraws on their change events, and talks
    /// out only through <see cref="OnBack"/>. A chest is opened on screen by <see cref="ChestOpening"/>,
    /// unchanged.
    /// </summary>
    public class LevelPathMenu : MonoBehaviour
    {
        private VisualElement root;
        private ScrollView road;
        private Label levelPill, xpLabel;
        private VisualElement xpFill;
        private UiCoinChip coinChip;
        private VisualElement claimAll;
        private Label claimAllCount;

        private VisualElement revealLayer;
        private VisualElement revealArt;
        private Label revealKicker, revealName, revealNote;

        private readonly List<ColumnView> columns = new List<ColumnView>();

        /// <summary>Raised when the player backs out.</summary>
        public Action OnBack;

        public bool IsOpen => root != null && root.style.display != DisplayStyle.None;

        /// <summary>Everything about one stop that changes after it is built.</summary>
        private class ColumnView
        {
            public int Level;
            public VisualElement Column;
            public UiFrame Card;
            public Color Tier;
            public VisualElement TrackLeft, TrackRight;
            public UiHex Marker;
            public Label MarkerText;
            public VisualElement Foot;       // the state line of a big card
            public Label FootLabel;
            public VisualElement Corner;     // the check / lock on a small card
            public UiIcon CornerIcon;
        }

        public void Build(Transform canvasRoot)
        {
            root = UiKit.El("screen levelroad", UiToolkitHost.Root, "LevelRoad");
            var sheet = Resources.Load<StyleSheet>("UI/Styles/LevelRoad");
            if (sheet != null) root.styleSheets.Add(sheet);
            root.pickingMode = PickingMode.Position;

            UiBackdrop.AddTo(root);

            BuildHeader();
            BuildStrip();
            BuildRoad();
            BuildReveal();

            UiFonts.Apply(root);
            UiKit.Show(root, false);
        }

        // ---------- build ----------

        private void BuildHeader()
        {
            var header = UiKit.El("lr__header", root);

            var back = UiKit.Frame("sq lr__back", header);
            back.Add(new UiIcon(UiIcon.Glyph.ChevronLeft, ArcadeTheme.Ink, 2.5f));
            UiKit.OnTap(back, Back, UiSound.Back);

            var box = UiKit.El("lr__title-box", header);
            var row = UiKit.El("lr__title-row", box);
            row.Add(new UiIcon(UiIcon.Glyph.Levels, ArcadeTheme.Pulse, 2f));
            UiKit.Text("LEVEL ROAD", "lr__title f-display", row);
            UiKit.El("lr__bar", box);
        }

        /// <summary>Where you are (level and XP), the coin balance, and CLAIM ALL.</summary>
        private void BuildStrip()
        {
            var strip = UiKit.El("lr__strip", root);
            levelPill = UiKit.Text("LV 1", "lr__pill f-display", strip);

            var bar = UiKit.El("bar lr__bar-track", strip);
            xpFill = UiKit.El("bar__fill", bar);
            xpLabel = UiKit.Text(string.Empty, "lr__xp f-display-semi", strip);
            coinChip = new UiCoinChip();
            strip.Add(coinChip);

            var title = UiKit.El("lr__row-head", root);
            title.Add(new UiIcon(UiIcon.Glyph.Levels, ArcadeTheme.Pulse, 2f));
            UiKit.Text("ALL REWARDS", "lr__row-title f-display", title);
            UiKit.El("lr__row-rule", title);

            claimAll = UiKit.Frame("lr__claimall", title);
            UiKit.Text("CLAIM ALL", "lr__claimall-label f-display", claimAll);
            var count = UiKit.El("lr__claimall-count", claimAll);
            claimAllCount = UiKit.Text("0", "lr__claimall-count-label f-display", count);
            UiKit.OnTap(claimAll, ClaimAllCoins, UiSound.Reward);
        }

        private void BuildRoad()
        {
            road = new ScrollView(ScrollViewMode.Horizontal) { name = "Road" };
            road.AddToClassList("lr__road");
            road.horizontalScrollerVisibility = ScrollerVisibility.Hidden;
            road.verticalScrollerVisibility = ScrollerVisibility.Hidden;
            road.touchScrollBehavior = ScrollView.TouchScrollBehavior.Elastic;
            road.contentContainer.AddToClassList("lr__stops");
            root.Add(road);
        }

        private void BuildColumns()
        {
            columns.Clear();
            road.contentContainer.Clear();

            for (int level = 2; level <= PlayerProgress.MaxLevel; level++)
            {
                columns.Add(BuildColumn(level));
            }

            UiFonts.Apply(road.contentContainer);
        }

        private ColumnView BuildColumn(int level)
        {
            LevelPath.Reward reward = LevelPath.For(level);
            bool big = reward.IsMilestone;

            var view = new ColumnView { Level = level };
            view.Column = UiKit.El("lvl" + (big ? " lvl--big" : " lvl--small"), road.contentContainer);

            view.Card = UiKit.Frame("lvl__card", view.Column);
            UiKit.OnTap(view.Card, () => ClaimLevel(level), UiSound.Reward);

            if (big) BuildBigCard(view, reward);
            else BuildSmallCard(view, reward);

            UiKit.El("lvl__stem", view.Column);

            var rail = UiKit.El("lvl__rail", view.Column);
            view.TrackLeft = UiKit.El("lvl__track lvl__track--left", rail);
            view.TrackRight = UiKit.El("lvl__track lvl__track--right", rail);
            view.Marker = new UiHex();
            view.Marker.AddToClassList("lvl__marker");
            view.MarkerText = UiKit.Text(level.ToString(), "lvl__marker-text f-display", view.Marker);
            rail.Add(view.Marker);

            RefreshColumn(view);
            return view;
        }

        private static void BuildSmallCard(ColumnView view, LevelPath.Reward reward)
        {
            view.Tier = ArcadeTheme.Reward;
            view.Card.pickingMode = PickingMode.Position;

            view.Card.Add(new UiCoin { name = "CardCoin" });
            UiKit.Text("+" + reward.Coins, "lvl__coins f-display", view.Card);

            view.Corner = UiKit.El("lvl__corner", view.Card);
            view.CornerIcon = new UiIcon(UiIcon.Glyph.Lock, ArcadeTheme.Dim, 2.6f);
            view.Corner.Add(view.CornerIcon);
        }

        private static void BuildBigCard(ColumnView view, LevelPath.Reward reward)
        {
            string tag, title;
            Color tier;
            VisualElement art;

            if (reward.Kind == LevelPath.RewardKind.Chest)
            {
                tier = UIFactory.ChestColor(reward.Tier);
                tag = CosmeticCatalog.RarityName((Rarity)(int)reward.Tier).ToUpperInvariant();
                title = "LUCKY CHEST";
                art = new UiChest(reward.Tier);
                art.AddToClassList("lvl__chest");
            }
            else
            {
                CosmeticItem item = CosmeticCatalog.Get(reward.CosmeticId);
                tier = UIFactory.RarityColor(item.Rarity);
                tag = item.Valid ? CosmeticCatalog.KindLabel(item.Kind).ToUpperInvariant() : "SKIN";
                title = item.Valid ? item.Name.ToUpperInvariant() : "SKIN";
                art = item.Valid ? UiArt.Cosmetic(item) : new VisualElement();
                art.AddToClassList("lvl__skin");
            }

            view.Tier = tier;

            var pill = UiKit.El("lvl__tag", view.Card);
            pill.style.backgroundColor = tier;
            UiKit.Text(tag, "lvl__tag-label f-display", pill);

            var stage = UiKit.El("lvl__stage", view.Card);
            stage.Add(art);

            UiKit.Text(title, "lvl__title f-display", view.Card);

            view.Foot = UiKit.El("lvl__foot", view.Card);
            view.FootLabel = UiKit.Text(string.Empty, "lvl__foot-label f-display", view.Foot);
        }

        // ---------- refresh ----------

        private void RefreshColumn(ColumnView view)
        {
            int level = view.Level;
            int reached = PlayerProgress.Level;
            bool unlocked = level <= reached;
            bool claimed = LevelPath.IsClaimed(level);
            bool claimable = unlocked && !claimed;
            bool big = view.Foot != null;

            // The rail is lit as far as the player has walked: this column's left half once they have
            // reached it, its right half once they have passed it.
            view.TrackLeft.EnableInClassList("is-lit", level <= reached);
            view.TrackRight.EnableInClassList("is-lit", level < reached);

            // The marker: the level you stand on is white, the ones behind you cyan, the rest dark.
            view.Marker.Edge = level == reached ? ArcadeTheme.Ink : unlocked ? ArcadeTheme.Pulse : ArcadeTheme.BgRaised;
            view.Marker.Fill = level == reached ? ArcadeTheme.Ink : unlocked ? ArcadeTheme.Pulse : ArcadeTheme.BgRaised;
            view.MarkerText.style.color = unlocked ? ArcadeTheme.OnPulse : ArcadeTheme.InkMuted;
            view.Marker.EnableInClassList("is-here", level == reached);

            // The card: gold and glowing when there is something to take; dim once taken; quiet while locked.
            view.Card.EnableInClassList("is-claimable", claimable);
            view.Card.EnableInClassList("is-claimed", claimed);
            view.Card.EnableInClassList("is-locked", !unlocked);
            view.Card.OverrideEdge(claimable ? ArcadeTheme.Reward : (big ? view.Tier : ArcadeTheme.LineLit));
            view.Card.OverrideFill(claimable ? new Color(0.082f, 0.075f, 0.051f) : claimed ? ArcadeTheme.BgDeep : ArcadeTheme.BgPanel);

            // Only a card with something to take responds to a tap; the others answer with the dull note.
            view.Card.EnableInClassList("is-disabled", !claimable);

            if (big)
            {
                view.Foot.EnableInClassList("lvl__foot--claim", claimable);
                view.FootLabel.text = claimable ? "CLAIM" : claimed ? "OWNED" : "LEVEL " + level;
            }
            else
            {
                view.CornerIcon.Kind = claimed ? UiIcon.Glyph.Check : UiIcon.Glyph.Lock;
                view.CornerIcon.Color = claimed ? ArcadeTheme.Pulse : ArcadeTheme.Dim;
                UiKit.Show(view.Corner, !claimable);
            }
        }

        private void RefreshAll()
        {
            for (int i = 0; i < columns.Count; i++) RefreshColumn(columns[i]);
            RefreshStrip();
        }

        private void RefreshStrip()
        {
            if (levelPill == null) return;

            levelPill.text = "LV " + PlayerProgress.Level;
            xpLabel.text = PlayerProgress.XpLabel;
            xpFill.style.width = Length.Percent(Mathf.Clamp01(PlayerProgress.XpFraction) * 100f);

            int coinLevels = 0;
            int top = Mathf.Min(PlayerProgress.Level, PlayerProgress.MaxLevel);
            for (int level = 2; level <= top; level++)
            {
                if (LevelPath.CanClaim(level) && !LevelPath.For(level).IsMilestone) coinLevels++;
            }

            UiKit.Show(claimAll, coinLevels > 0);
            claimAllCount.text = coinLevels.ToString();
        }

        // ---------- claiming ----------

        private void ClaimLevel(int level)
        {
            LevelPath.ClaimResult result = LevelPath.Claim(level);
            if (!result.Ok) return;

            RefreshAll();

            // Coins fly into the balance chip, which is already on screen — showing a card for them as
            // well would be a modal to dismiss for the most ordinary reward on the road. A skin or a
            // chest is worth stopping for, and a chest gets opened on screen; the coins it pays (only
            // when nothing is left to win) fly in once it closes, so the balance is held until then.
            if (result.FromChest)
            {
                HoldCoins(result.Coins);
                ChestOpening.Play(result.Tier, result.Item, result.Coins, () =>
                    RewardFly.Play(RewardFly.Kind.Coins, null, coinChip, result.Coins));
            }
            else if (result.Item.Valid)
            {
                ShowReveal(result);
            }
            else if (result.Coins > 0)
            {
                // Plain coins, or the refund for a skin already owned: from the card to the chip.
                VisualElement card = level - 2 < columns.Count ? columns[level - 2].Card : null;
                RewardFly.Play(RewardFly.Kind.Coins, card, coinChip, result.Coins);
            }
        }

        private void ClaimAllCoins()
        {
            int total = LevelPath.ClaimAllCoins();
            RefreshAll();
            if (total > 0) RewardFly.Play(RewardFly.Kind.Coins, claimAll, coinChip, total);
        }

        /// <summary>Keeps the chip at the balance from before a reward that is about to be shown, so the
        /// fly-in that follows counts up from there instead of the number having moved already.</summary>
        private void HoldCoins(int coins)
        {
            if (coins <= 0) return;
            coinChip.Freeze(true);
            coinChip.SetNow(Mathf.Max(0, coinChip.Target - coins));
        }

        // ---------- reveal ----------

        /// <summary>The card shown when a named skin pays out: the thing won, big, with its rarity.</summary>
        private void BuildReveal()
        {
            revealLayer = UiKit.El("bleed lr__reveal-layer", root);
            revealLayer.pickingMode = PickingMode.Position;

            var card = UiKit.Frame("lr__reveal", revealLayer);
            revealKicker = UiKit.Text("REWARD", "lr__reveal-kicker f-display", card);
            revealArt = UiKit.El("lr__reveal-art", card);
            revealName = UiKit.Text(string.Empty, "lr__reveal-name f-display", card);
            revealNote = UiKit.Text(string.Empty, "lr__reveal-note f-display", card);
            UiKit.Button("NICE", "pulse", HideReveal, card, "lr__reveal-button");

            UiKit.Show(revealLayer, false);
        }

        private void ShowReveal(LevelPath.ClaimResult result)
        {
            Color rarity = UIFactory.RarityColor(result.Item.Rarity);

            revealArt.Clear();
            var art = UiArt.Cosmetic(result.Item);
            art.AddToClassList("lr__reveal-picture");
            revealArt.Add(art);

            revealKicker.text = "LEVEL " + result.Level;
            revealKicker.style.color = rarity;
            revealName.text = result.Item.Name;
            revealNote.text = CosmeticCatalog.RarityName(result.Item.Rarity).ToUpperInvariant() + " · " +
                              CosmeticCatalog.KindLabel(result.Item.Kind).ToUpperInvariant();
            revealNote.style.color = rarity;

            UiFonts.Apply(revealLayer);
            UiKit.Show(revealLayer, true);
            revealLayer.style.opacity = 0f;
            UiKit.Fade(revealLayer, 1f, ArcadeTheme.TFast);
        }

        private void HideReveal() => UiKit.Show(revealLayer, false);

        // ---------- state ----------

        public void Open()
        {
            if (root == null) return;

            PlayerProgress.OnChanged -= RefreshAll;
            PlayerProgress.OnChanged += RefreshAll;
            LevelPath.OnChanged -= RefreshAll;
            LevelPath.OnChanged += RefreshAll;

            // Built once, on the first visit, and kept: fifty-nine columns is a lot of elements to make
            // at boot for a player who never opens the road, and RefreshAll repaints every one of them.
            if (columns.Count == 0) BuildColumns();

            // A chest left mid-opening by a screen change must not leave the balance held.
            coinChip.Freeze(false);
            HideReveal();
            RefreshAll();

            UiKit.Show(root, true);
            root.BringToFront();
            root.style.opacity = 0f;
            UiKit.Fade(root, 1f, ArcadeTheme.TFast);
            GameSfx.PlayUi(UiSound.Open);

            // Park the view on the level the player is at, rather than at level 2. Deferred: layout has
            // not run yet at the moment Open() finishes, so a column's position is not known.
            road.schedule.Execute(ScrollToCurrent).StartingIn(60);
        }

        private void ScrollToCurrent()
        {
            int index = Mathf.Clamp(PlayerProgress.Level - 2, 0, columns.Count - 1);
            if (columns.Count == 0) return;

            float x = columns[index].Column.layout.x;
            // The current column a little in from the left edge, with the next rewards showing beyond it.
            road.scrollOffset = new Vector2(Mathf.Max(0f, x - 150f), 0f);
        }

        public void Close()
        {
            PlayerProgress.OnChanged -= RefreshAll;
            LevelPath.OnChanged -= RefreshAll;
            if (IsOpen) ChestOpening.Cancel();

            if (root != null) UiKit.Show(root, false);
        }

        private void OnDestroy()
        {
            PlayerProgress.OnChanged -= RefreshAll;
            LevelPath.OnChanged -= RefreshAll;
        }

        private void Back()
        {
            Close();
            OnBack?.Invoke();
        }
    }
}
