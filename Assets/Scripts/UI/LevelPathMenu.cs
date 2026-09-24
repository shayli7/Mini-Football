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
    /// The level path: every level from 2 to <see cref="PlayerProgress.MaxLevel"/> as a rung on a
    /// vertical road, with the reward it pays and a button to collect it.
    ///
    /// The road is drawn by the rows themselves. Each one carries a segment of the spine behind its
    /// node, lit gold up to the level reached and grey past it, so the path is continuous by
    /// construction rather than by a separate line measured to match — which could only ever drift out
    /// of step with the rows it was meant to connect.
    ///
    /// Rows are built ONCE and then refreshed in place. Claiming rebuilds nothing: the row that
    /// changed repaints, and the rest sit still. Rebuilding sixty rows on every tap would rebuild the
    /// scroll position along with them and throw the player back to the top of the path each time they
    /// collected something.
    ///
    /// Like every other screen it reads its data from <c>Net/</c> (<see cref="PlayerProgress"/> for the
    /// level, <see cref="LevelPath"/> for the rewards and claims), redraws on their change events, and
    /// talks out only through <see cref="OnBack"/>.
    /// </summary>
    public class LevelPathMenu : MonoBehaviour
    {
        private GameObject root;
        private CanvasGroup group;
        private ScrollRect scroll;
        private RectTransform listContent;

        private TextMeshProUGUI levelLabel;
        private TextMeshProUGUI xpLabel;
        private UIFillBar xpFill;
        private MenuButton claimAllButton;

        private GameObject revealPanel;
        private Transform revealArt;
        private TextMeshProUGUI revealKicker;
        private TextMeshProUGUI revealName;
        private TextMeshProUGUI revealNote;

        private readonly List<RowView> rows = new List<RowView>();
        private Coroutine scrollRoutine;

        /// <summary>Raised when the player backs out.</summary>
        public Action OnBack;

        public bool IsOpen => root != null && root.activeSelf;

        // Row heights. A milestone is visibly taller than a coin level — that size difference IS the
        // "big milestone", and it is what lets a player flick down the path and see where the next
        // real prize sits without reading anything.
        private const float CoinRowHeight = 62f;
        private const float MilestoneRowHeight = 104f;
        private const float NodeColumn = 96f;

        /// <summary>
        /// Everything about one rung that changes after it is built. Held rather than looked up,
        /// because a claim has to repaint exactly one row and finding it by name in a tree of sixty
        /// would be both slower and easy to get subtly wrong.
        /// </summary>
        private class RowView
        {
            public int Level;
            public Image Background;
            public Image SpineUp;
            public Image SpineDown;
            public Image NodeRing;
            public Image NodeFace;
            public TextMeshProUGUI NodeText;
            public TextMeshProUGUI RewardText;
            public MenuButton Claim;
            public GameObject ClaimHolder;
            public TextMeshProUGUI StateText;
        }

        public void Build(Transform canvasRoot)
        {
            root = UIFactory.Child(canvasRoot, "LevelPathMenu");
            UIFactory.Stretch(UIFactory.Rt(root));
            group = root.AddComponent<CanvasGroup>();
            UIFactory.ScrimDim(root.transform);

            UIFactory.ScreenHeader(root.transform, "Level Path", Back);

            var panel = UIFactory.Panel(root.transform, "PathPanel");
            var prt = UIFactory.Rt(panel);
            prt.anchorMin = prt.anchorMax = new Vector2(0.5f, 0.5f);
            prt.pivot = new Vector2(0.5f, 0.5f);
            // Short enough to clear the header on a 20:9 phone, where the canvas is only ~805 tall.
            prt.sizeDelta = new Vector2(880f, 680f);
            prt.anchoredPosition = new Vector2(0f, -40f);

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
            BuildList(content.transform);
            BuildFooter(content.transform);

            // Over the whole screen, not inside the panel: a reveal that opened inside the list would
            // be clipped by the scroll view's own mask.
            BuildReveal(root.transform);

            // The rows themselves are NOT built here. Fifty-nine of them is roughly nine hundred
            // GameObjects, and this runs at boot alongside every other screen — a player who never
            // opens the path would pay for it on the loading screen anyway. They are built the first
            // time the path is opened instead, and kept after that. See Open.
            root.SetActive(false);
        }

        /// <summary>
        /// Who you are on the path: the level you have reached, how far into the next one you are, and
        /// how many coins you are carrying. The same XP bar the profile chip draws, so a level reads
        /// identically wherever the player meets it.
        /// </summary>
        private void BuildHeader(Transform parent)
        {
            var header = UIFactory.Child(parent, "Header");
            header.AddComponent<LayoutElement>().preferredHeight = 76f;

            var h = header.AddComponent<HorizontalLayoutGroup>();
            h.spacing = ArcadeTheme.Md;
            h.childAlignment = TextAnchor.MiddleLeft;
            h.childForceExpandWidth = false;
            h.childForceExpandHeight = true;
            h.childControlWidth = true;
            h.childControlHeight = true;

            var levelCell = UIFactory.Child(header.transform, "LevelCell");
            var lle = levelCell.AddComponent<LayoutElement>();
            lle.preferredWidth = 120f;
            lle.minWidth = 120f;
            UIFactory.RoundedImage(levelCell, ArcadeTheme.RadMd, ArcadeTheme.Gold.WithAlpha(0.16f), false);
            levelLabel = UIFactory.Text(levelCell.transform, "LV —", ArcadeTheme.FsBody,
                                        ArcadeTheme.Gold, display: true, bold: true, upper: true,
                                        tracking: 3f);
            UIFactory.Stretch(UIFactory.Rt(levelLabel.gameObject), 0);

            var xpCell = UIFactory.Child(header.transform, "XpCell");
            xpCell.AddComponent<LayoutElement>().flexibleWidth = 1f;
            var xpCol = xpCell.AddComponent<VerticalLayoutGroup>();
            xpCol.childAlignment = TextAnchor.MiddleCenter;
            xpCol.childForceExpandWidth = true;
            xpCol.childForceExpandHeight = false;
            xpCol.childControlWidth = true;
            xpCol.childControlHeight = true;

            var xp = UIFactory.XpBar(xpCell.transform, PlayerProgress.XpFraction, "0 / 0 XP", 30f);
            xpFill = xp.GetComponentInChildren<UIFillBar>();
            xpLabel = xp.GetComponentInChildren<TextMeshProUGUI>();

            var purse = UIFactory.Child(header.transform, "Purse");
            var ple = purse.AddComponent<LayoutElement>();
            ple.preferredWidth = 200f;
            ple.minWidth = 200f;
            // On a child of the cell, not on the cell — Build reparents its own GameObject to what it
            // is given, and a transform cannot be its own parent.
            UIFactory.Child(purse.transform, "Pill").AddComponent<CoinPill>().Build(purse.transform);
        }

        private void BuildList(Transform parent)
        {
            var holder = UIFactory.Child(parent, "ListHolder");
            var le = holder.AddComponent<LayoutElement>();
            le.preferredHeight = 470f;
            le.flexibleHeight = 1f;
            le.minHeight = 160f;

            // No spacing: the rows carry the spine between them, and a gap would cut the road into
            // sixty separate pieces.
            listContent = UIFactory.ScrollList(holder.transform, 0f);
            scroll = holder.GetComponentInChildren<ScrollRect>();
        }

        private void BuildFooter(Transform parent)
        {
            var row = UIFactory.Child(parent, "Footer");
            row.AddComponent<LayoutElement>().preferredHeight = 62f;

            var h = row.AddComponent<HorizontalLayoutGroup>();
            h.spacing = ArcadeTheme.Sm;
            h.childForceExpandWidth = true;
            h.childForceExpandHeight = true;
            h.childControlWidth = true;
            h.childControlHeight = true;

            claimAllButton = UIFactory.Button(row.transform, "Claim All Coins",
                                              MenuButton.Variant.Primary, ClaimAllCoins);
            UIShine.AddTo(claimAllButton);
        }

        // ---------- rows ----------

        private void BuildRows()
        {
            rows.Clear();
            UIFactory.ClearChildren(listContent);

            for (int level = 2; level <= PlayerProgress.MaxLevel; level++)
            {
                rows.Add(BuildRow(level));
            }
        }

        private RowView BuildRow(int level)
        {
            LevelPath.Reward reward = LevelPath.For(level);
            bool milestone = reward.IsMilestone;
            float height = milestone ? MilestoneRowHeight : CoinRowHeight;

            var view = new RowView { Level = level };

            var row = UIFactory.Child(listContent, "Level" + level);
            row.AddComponent<LayoutElement>().preferredHeight = height;

            // Raycastable, and that is load-bearing rather than decorative: with nothing else in the
            // scroll view to hit, a non-raycast row leaves no surface for a drag to land on and the
            // list cannot be scrolled at all. The row never handles the event; it only has to be hit
            // so the drag can bubble up to the ScrollRect. Same reason LeagueMenu's rows do it.
            view.Background = UIFactory.RoundedImage(row, ArcadeTheme.RadSm, Color.clear, true);

            var h = row.AddComponent<HorizontalLayoutGroup>();
            h.padding = new RectOffset(8, 12, 0, 0);
            h.spacing = ArcadeTheme.Md;
            h.childAlignment = TextAnchor.MiddleLeft;
            h.childForceExpandWidth = false;
            h.childForceExpandHeight = true;
            h.childControlWidth = true;
            h.childControlHeight = true;

            BuildNodeColumn(row.transform, view, level, reward, height);
            BuildRewardColumn(row.transform, view, reward);
            BuildStateColumn(row.transform, view, level);

            RefreshRow(view);
            return view;
        }

        /// <summary>
        /// The node and its two half-segments of road — one reaching up to the row above, one down to
        /// the row below. Two halves rather than one full-height bar because the node sits in the
        /// middle: the segment above it is lit by whether the PREVIOUS level was reached, and the one
        /// below by this one, which is what makes the road stop exactly at the player rather than at
        /// the nearest row boundary.
        /// </summary>
        private void BuildNodeColumn(Transform parent, RowView view, int level,
                                     LevelPath.Reward reward, float rowHeight)
        {
            var column = UIFactory.Child(parent, "Node");
            var cle = column.AddComponent<LayoutElement>();
            cle.preferredWidth = NodeColumn;
            cle.minWidth = NodeColumn;

            float half = rowHeight * 0.5f;

            view.SpineUp = SpineSegment(column.transform, "SpineUp", half, 1f);
            view.SpineDown = SpineSegment(column.transform, "SpineDown", half, 0f);

            // The top of the road has nothing above it and the bottom nothing below, so those two
            // stubs would be a line running off into empty space.
            if (level == 2) view.SpineUp.gameObject.SetActive(false);
            if (level == PlayerProgress.MaxLevel) view.SpineDown.gameObject.SetActive(false);

            float size = reward.IsMilestone ? 66f : 46f;

            var node = UIFactory.Child(column.transform, "Disc");
            var nrt = UIFactory.Rt(node);
            nrt.anchorMin = nrt.anchorMax = new Vector2(0.5f, 0.5f);
            nrt.pivot = new Vector2(0.5f, 0.5f);
            nrt.sizeDelta = new Vector2(size, size);

            var ring = UIFactory.Child(node.transform, "Ring");
            view.NodeRing = ring.AddComponent<Image>();
            view.NodeRing.sprite = ArcadeTheme.Disc();
            view.NodeRing.raycastTarget = false;
            UIFactory.Stretch(UIFactory.Rt(ring), 0);

            var face = UIFactory.Child(node.transform, "Face");
            view.NodeFace = face.AddComponent<Image>();
            view.NodeFace.sprite = ArcadeTheme.Disc();
            view.NodeFace.raycastTarget = false;
            UIFactory.Stretch(UIFactory.Rt(face), 3.5f);

            view.NodeText = UIFactory.Text(node.transform, level.ToString(),
                                           size * (reward.IsMilestone ? 0.40f : 0.44f),
                                           ArcadeTheme.Ink, display: true, bold: true, upper: false,
                                           tracking: 0f, richText: false);
            UIFactory.Stretch(UIFactory.Rt(view.NodeText.gameObject), 0);
        }

        /// <summary>Half the road, above or below the node. <paramref name="anchorY"/> is 1 for the
        /// upper half and 0 for the lower.</summary>
        private static Image SpineSegment(Transform parent, string name, float length, float anchorY)
        {
            var go = UIFactory.Child(parent, name);
            var img = UIFactory.RoundedImage(go, 2, ArcadeTheme.Line, false);
            var rt = UIFactory.Rt(go);
            rt.anchorMin = new Vector2(0.5f, anchorY);
            rt.anchorMax = new Vector2(0.5f, anchorY);
            rt.pivot = new Vector2(0.5f, anchorY);
            rt.sizeDelta = new Vector2(7f, length);
            rt.anchoredPosition = Vector2.zero;
            return img;
        }

        /// <summary>The prize itself: its glyph, then what it is in words.</summary>
        private void BuildRewardColumn(Transform parent, RowView view, LevelPath.Reward reward)
        {
            var column = UIFactory.Child(parent, "Reward");
            column.AddComponent<LayoutElement>().flexibleWidth = 1f;

            var h = column.AddComponent<HorizontalLayoutGroup>();
            h.spacing = ArcadeTheme.Md;
            h.childAlignment = TextAnchor.MiddleLeft;
            h.childForceExpandWidth = false;
            h.childForceExpandHeight = false;
            h.childControlWidth = true;
            h.childControlHeight = true;

            float art = reward.IsMilestone ? 74f : 38f;
            var artCell = UIFactory.Child(column.transform, "Art");
            var ale = artCell.AddComponent<LayoutElement>();
            ale.preferredWidth = art;
            ale.minWidth = art;
            ale.preferredHeight = art;

            string caption;
            Color captionColor;

            switch (reward.Kind)
            {
                case LevelPath.RewardKind.Chest:
                    UIFactory.ChestGlyph(artCell.transform, reward.Tier, art);
                    caption = ChestLoot.TierName(reward.Tier).ToUpperInvariant();
                    captionColor = UIFactory.ChestColor(reward.Tier);
                    break;

                case LevelPath.RewardKind.Skin:
                {
                    CosmeticItem item = CosmeticCatalog.Get(reward.CosmeticId);
                    var frame = UIFactory.Child(artCell.transform, "Frame");
                    UIFactory.Stretch(UIFactory.Rt(frame), 0);
                    UIFactory.CosmeticSwatch(frame.transform, item);
                    caption = item.Valid
                        ? $"{item.Name.ToUpperInvariant()}  ·  {CosmeticCatalog.KindLabel(item.Kind).ToUpperInvariant()}"
                        : "SKIN";
                    captionColor = UIFactory.RarityColor(item.Rarity);
                    break;
                }

                default:
                    UIFactory.CoinGlyph(artCell.transform, art);
                    caption = $"{reward.Coins} COINS";
                    captionColor = ArcadeTheme.Coin;
                    break;
            }

            view.RewardText = UIFactory.Text(column.transform, caption,
                                             reward.IsMilestone ? ArcadeTheme.FsBody
                                                                : ArcadeTheme.FsCaption,
                                             captionColor, display: true, bold: true, upper: true,
                                             tracking: 2f, align: TextAlignmentOptions.Left,
                                             richText: false);
            view.RewardText.gameObject.AddComponent<LayoutElement>().flexibleWidth = 1f;
            view.RewardText.overflowMode = TextOverflowModes.Ellipsis;
        }

        /// <summary>
        /// The right-hand end of the row: a Claim button when there is something to take, and a plain
        /// word when there is not. Both are built and one is hidden, rather than one being created on
        /// demand — a button made and destroyed as the player levels up would be a listener to leak
        /// once per level.
        /// </summary>
        private void BuildStateColumn(Transform parent, RowView view, int level)
        {
            var column = UIFactory.Child(parent, "State");
            var cle = column.AddComponent<LayoutElement>();
            cle.preferredWidth = 168f;
            cle.minWidth = 168f;

            view.StateText = UIFactory.Text(column.transform, string.Empty,
                                            ArcadeTheme.FsCaption * 0.88f, ArcadeTheme.InkMuted,
                                            display: false, bold: true, upper: true, tracking: 3f);
            UIFactory.Stretch(UIFactory.Rt(view.StateText.gameObject), 0);

            view.ClaimHolder = UIFactory.Child(column.transform, "ClaimHolder");
            var crt = UIFactory.Rt(view.ClaimHolder);
            crt.anchorMin = new Vector2(0f, 0.5f);
            crt.anchorMax = new Vector2(1f, 0.5f);
            crt.pivot = new Vector2(0.5f, 0.5f);
            crt.sizeDelta = new Vector2(0f, 46f);
            crt.anchoredPosition = Vector2.zero;

            var v = view.ClaimHolder.AddComponent<VerticalLayoutGroup>();
            v.childForceExpandWidth = true;
            v.childForceExpandHeight = true;
            v.childControlWidth = true;
            v.childControlHeight = true;

            view.Claim = UIFactory.Button(view.ClaimHolder.transform, "Claim",
                                          MenuButton.Variant.Primary, () => ClaimLevel(level), 46f);
        }

        /// <summary>
        /// Repaints one row for the current level and claim state. The single place a rung's four
        /// appearances — locked, ready, collected, and the road behind it — are decided, so they can
        /// never disagree about which one is showing.
        /// </summary>
        private void RefreshRow(RowView view)
        {
            int level = view.Level;
            int reached = PlayerProgress.Level;
            bool unlocked = level <= reached;
            bool claimed = LevelPath.IsClaimed(level);
            bool claimable = unlocked && !claimed;
            LevelPath.Reward reward = LevelPath.For(level);

            Color accent = reward.Kind == LevelPath.RewardKind.Chest
                ? UIFactory.ChestColor(reward.Tier)
                : ArcadeTheme.Gold;

            // The road is lit as far as the player has walked. The half ABOVE the node belongs to the
            // step from the previous level, so it lights one level earlier than the half below it.
            if (view.SpineUp != null)
            {
                view.SpineUp.color = level - 1 <= reached ? ArcadeTheme.Gold : ArcadeTheme.Line;
            }
            if (view.SpineDown != null)
            {
                view.SpineDown.color = unlocked ? ArcadeTheme.Gold : ArcadeTheme.Line;
            }

            // A passed level is a solid blue node with its number in ink. It used to be gold text on
            // a translucent gold face over a gold ring, which blended into one gold blob and lost the
            // number — so the rows behind the player read as unlabelled dots. The level the player is
            // standing on keeps a gold ring, so "here" is still marked when nothing is waiting.
            view.NodeRing.color = claimable ? accent
                                : level == reached ? ArcadeTheme.Gold
                                : unlocked ? ArcadeTheme.BlueLine
                                : ArcadeTheme.Line;
            view.NodeFace.color = claimable ? accent
                                : unlocked ? ArcadeTheme.BlueFill
                                : ArcadeTheme.BgDeep;
            view.NodeText.color = claimable ? ArcadeTheme.OnGold
                                : unlocked ? ArcadeTheme.Ink
                                : ArcadeTheme.InkMuted;

            // The row a player is standing on gets a faint band behind it, so flicking down the path
            // shows where "here" is without hunting for the first grey node.
            view.Background.color = level == reached ? ArcadeTheme.Gold.WithAlpha(0.07f) : Color.clear;

            // Collected rewards recede. They are still readable — the path is also a record of what
            // has been won — but they must not compete with the one row that can still be tapped.
            var dim = view.RewardText;
            if (dim != null) dim.alpha = claimed ? 0.45f : 1f;

            view.ClaimHolder.SetActive(claimable);
            view.StateText.gameObject.SetActive(!claimable);
            view.StateText.text = claimed ? "Claimed" : $"Level {level}";
            view.StateText.color = claimed ? ArcadeTheme.BlueSoft : ArcadeTheme.InkMuted;
        }

        private void RefreshAll()
        {
            for (int i = 0; i < rows.Count; i++)
            {
                RefreshRow(rows[i]);
            }

            RefreshHeader();
        }

        private void RefreshHeader()
        {
            if (levelLabel == null)
            {
                return;
            }

            levelLabel.text = $"LV {PlayerProgress.Level}";

            if (xpLabel != null) xpLabel.text = PlayerProgress.XpLabel;

            if (xpFill != null) xpFill.SetFraction(PlayerProgress.XpFraction);

            if (claimAllButton != null)
            {
                claimAllButton.interactable = LevelPath.HasClaimableCoins();
            }
        }

        // ---------- claiming ----------

        private void ClaimLevel(int level)
        {
            LevelPath.ClaimResult result = LevelPath.Claim(level);
            if (!result.Ok)
            {
                return;
            }

            RefreshAll();

            // Coins land in the pill, which counts up on its own and is already on screen — showing a
            // card for them as well would be a modal dialog to dismiss for the most ordinary reward on
            // the path. A skin or a chest is worth stopping for, and a chest gets opened on screen.
            if (result.FromChest)
            {
                OpenChest(result);
            }
            else if (result.Item.Valid)
            {
                ShowReveal(result);
            }
        }

        /// <summary>
        /// Plays the chest opening over the path. The path itself is faded out and stops taking
        /// touches while it runs: the opening is on the UI Toolkit panel and this screen is still on
        /// the uGUI canvas, and which of the two draws on top is not something to rely on.
        /// </summary>
        private void OpenChest(LevelPath.ClaimResult result)
        {
            group.alpha = 0f;
            group.blocksRaycasts = false;

            ChestOpening.Play(result.Tier, result.Item, result.Coins, () =>
            {
                if (!IsOpen) return;
                group.alpha = 1f;
                group.blocksRaycasts = true;
            });
        }

        private void ClaimAllCoins()
        {
            int coins = LevelPath.ClaimAllCoins();
            RefreshAll();

            // No card here either, for the same reason — but the button that did it has to stop
            // offering, and RefreshAll has already turned it off if that was the last one.
            if (coins <= 0 && claimAllButton != null)
            {
                claimAllButton.interactable = false;
            }
        }

        // ---------- reveal ----------

        /// <summary>
        /// The card shown when a milestone pays out: the thing won, big, with its rarity and where it
        /// came from. Built once and refilled, so a chest opened forty levels apart is the same card.
        /// </summary>
        private void BuildReveal(Transform parent)
        {
            revealPanel = UIFactory.Child(parent, "Reveal");
            UIFactory.Stretch(UIFactory.Rt(revealPanel));
            UIFactory.ScrimDim(revealPanel.transform, 0.72f);

            var panel = UIFactory.Panel(revealPanel.transform, "RevealPanel");
            var prt = UIFactory.Rt(panel);
            prt.anchorMin = prt.anchorMax = new Vector2(0.5f, 0.5f);
            prt.pivot = new Vector2(0.5f, 0.5f);
            prt.sizeDelta = new Vector2(560f, 560f);

            var fill = panel.transform.Find("Fill");
            var col = UIFactory.Child(fill, "Col");
            UIFactory.Stretch(UIFactory.Rt(col), 28f);

            var v = col.AddComponent<VerticalLayoutGroup>();
            v.spacing = ArcadeTheme.Md;
            v.childAlignment = TextAnchor.UpperCenter;
            v.childForceExpandWidth = true;
            v.childForceExpandHeight = false;
            v.childControlWidth = true;
            v.childControlHeight = true;

            revealKicker = UIFactory.Text(col.transform, "REWARD", ArcadeTheme.FsCaption,
                                          ArcadeTheme.InkMuted, display: false, bold: true,
                                          upper: true, tracking: 6f);
            revealKicker.gameObject.AddComponent<LayoutElement>().preferredHeight = 26f;

            var art = UIFactory.Child(col.transform, "Art");
            art.AddComponent<LayoutElement>().preferredHeight = 240f;
            revealArt = art.transform;

            revealName = UIFactory.Text(col.transform, string.Empty, ArcadeTheme.FsBody * 1.2f,
                                        ArcadeTheme.Ink, display: true, bold: true, upper: false,
                                        tracking: 1f, richText: false);
            revealName.gameObject.AddComponent<LayoutElement>().preferredHeight = 38f;

            revealNote = UIFactory.Text(col.transform, string.Empty, ArcadeTheme.FsCaption,
                                        ArcadeTheme.InkMuted, display: false, bold: true, upper: true,
                                        tracking: 3f, richText: false);
            revealNote.gameObject.AddComponent<LayoutElement>().preferredHeight = 24f;

            UIFactory.Spacer(col.transform, 6f);

            var buttonRow = UIFactory.Child(col.transform, "ButtonRow");
            buttonRow.AddComponent<LayoutElement>().preferredHeight = 62f;
            var bv = buttonRow.AddComponent<VerticalLayoutGroup>();
            bv.childForceExpandWidth = true;
            bv.childForceExpandHeight = true;
            bv.childControlWidth = true;
            bv.childControlHeight = true;
            UIFactory.Button(buttonRow.transform, "Nice", MenuButton.Variant.Primary, HideReveal);

            revealPanel.SetActive(false);
        }

        private void ShowReveal(LevelPath.ClaimResult result)
        {
            if (revealPanel == null)
            {
                return;
            }

            UIFactory.ClearChildren(revealArt);

            string kicker = result.FromChest
                ? ChestLoot.TierName(result.Tier).ToUpperInvariant()
                : $"LEVEL {result.Level}";

            if (result.Item.Valid)
            {
                var frame = UIFactory.Child(revealArt, "Frame");
                var frt = UIFactory.Rt(frame);
                frt.anchorMin = frt.anchorMax = new Vector2(0.5f, 0.5f);
                frt.pivot = new Vector2(0.5f, 0.5f);
                frt.sizeDelta = new Vector2(230f, 230f);
                UIFactory.CosmeticSwatch(frame.transform, result.Item);

                revealKicker.text = kicker;
                revealKicker.color = UIFactory.RarityColor(result.Item.Rarity);
                revealName.text = result.Item.Name;
                revealNote.text = $"{CosmeticCatalog.RarityName(result.Item.Rarity)} " +
                                  $"{CosmeticCatalog.KindLabel(result.Item.Kind)}";
                revealNote.color = UIFactory.RarityColor(result.Item.Rarity);
            }
            else
            {
                UIFactory.CoinGlyph(revealArt, 200f);

                revealKicker.text = kicker;
                revealKicker.color = ArcadeTheme.Coin;
                revealName.text = $"{result.Coins} coins";
                // The one case worth explaining: a chest with nothing left to give is not a bad roll,
                // and a player who is not told that will read it as one.
                revealNote.text = result.FromChest ? "Every skin in this chest is already yours"
                                                   : "Added to your wallet";
                revealNote.color = ArcadeTheme.InkMuted;
            }

            revealPanel.SetActive(true);
            revealPanel.transform.SetAsLastSibling();
            StartCoroutine(UITween.PopIn(revealArt, ArcadeTheme.TSlow));
        }

        private void HideReveal()
        {
            if (revealPanel != null) revealPanel.SetActive(false);
        }

        // ---------- state ----------

        public void Open()
        {
            if (root == null) return;

            PlayerProgress.OnChanged -= RefreshAll;
            PlayerProgress.OnChanged += RefreshAll;
            LevelPath.OnChanged -= RefreshAll;
            LevelPath.OnChanged += RefreshAll;

            // Built once, on the first visit, and kept. Rebuilding them per open would also rebuild
            // the scroll position, and the rows carry no state of their own that could go stale —
            // RefreshAll repaints every one of them below.
            if (rows.Count == 0) BuildRows();

            HideReveal();

            root.SetActive(true);
            root.transform.SetAsLastSibling();
            group.alpha = 1f;
            group.blocksRaycasts = true;

            RefreshAll();

            if (scrollRoutine != null) StopCoroutine(scrollRoutine);
            scrollRoutine = StartCoroutine(ScrollToCurrent());
        }

        /// <summary>
        /// Parks the view on the level the player is actually at, rather than at level 2.
        ///
        /// Deferred by a frame and preceded by a forced canvas update: the content's height comes from
        /// a ContentSizeFitter, which has not run at the moment Open() finishes, and a normalized
        /// scroll position set against a zero-height content is silently discarded.
        /// </summary>
        private IEnumerator ScrollToCurrent()
        {
            yield return null;

            if (scroll != null)
            {
                Canvas.ForceUpdateCanvases();

                // The path runs top (level 2) to bottom (max), and verticalNormalizedPosition is 1 at
                // the top — hence the inversion. Biased slightly up the list so the current level sits
                // a little above centre, with the next rewards visible under it, which is the half a
                // player has come to look at.
                float span = Mathf.Max(1, PlayerProgress.MaxLevel - 2);
                float walked = Mathf.Clamp01((PlayerProgress.Level - 2f) / span);
                scroll.verticalNormalizedPosition = Mathf.Clamp01(1f - walked + 0.12f);
            }

            scrollRoutine = null;
        }

        public void Close()
        {
            PlayerProgress.OnChanged -= RefreshAll;
            LevelPath.OnChanged -= RefreshAll;
            ChestOpening.Cancel();

            if (root != null)
            {
                group.blocksRaycasts = false;
                root.SetActive(false);
            }
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
