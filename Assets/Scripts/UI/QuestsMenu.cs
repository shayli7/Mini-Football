using System;
using TableFootball.Progression;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace TableFootball.UI
{
    /// <summary>
    /// Today's three quests, the level they feed, and how long the set has left.
    ///
    /// A read-only screen on purpose: there is no Claim button anywhere in it. A claim step on a
    /// phone is a chore, and a quest left unclaimed overnight is a sour surprise — so quests bank
    /// themselves the moment they are cleared, in front of the player, on the result screen. This is
    /// where they come to check, not to collect.
    ///
    /// Laid out wide rather than tall, like <see cref="ProfileMenu"/>, because a landscape phone is
    /// the shape this game is actually held in.
    /// </summary>
    public class QuestsMenu : MonoBehaviour
    {
        private GameObject root;
        private CanvasGroup group;

        private Transform cardHost;
        private TextMeshProUGUI resetLabel;
        private TextMeshProUGUI levelNumber;
        private TextMeshProUGUI levelCaption;
        private TextMeshProUGUI streakLabel;
        private Image levelRing;
        private Image xpFill;

        private Transform slamPips;
        private TextMeshProUGUI slamNote;

        private float nextTick;

        /// <summary>Raised when the player backs out.</summary>
        public Action OnBack;

        public bool IsOpen => root != null && root.activeSelf;

        public void Build(Transform canvasRoot)
        {
            root = UIFactory.Child(canvasRoot, "QuestsMenu");
            UIFactory.Stretch(UIFactory.Rt(root));
            group = root.AddComponent<CanvasGroup>();

            UIFactory.Backdrop(root.transform);

            var title = UIFactory.Text(root.transform, "DAILY QUESTS", ArcadeTheme.FsTitle,
                                       ArcadeTheme.Ink, display: true, bold: true, upper: true,
                                       tracking: 8f);
            var trt = UIFactory.Rt(title.gameObject);
            trt.anchorMin = new Vector2(0f, 1f);
            trt.anchorMax = new Vector2(1f, 1f);
            trt.pivot = new Vector2(0.5f, 1f);
            trt.offsetMin = new Vector2(0f, -116f);
            trt.offsetMax = new Vector2(0f, -46f);

            resetLabel = UIFactory.Text(root.transform, string.Empty, ArcadeTheme.FsCaption,
                                        ArcadeTheme.InkMuted, display: false, bold: true,
                                        upper: true, tracking: 6f);
            var rrt = UIFactory.Rt(resetLabel.gameObject);
            rrt.anchorMin = new Vector2(0f, 1f);
            rrt.anchorMax = new Vector2(1f, 1f);
            rrt.pivot = new Vector2(0.5f, 1f);
            rrt.offsetMin = new Vector2(0f, -140f);
            rrt.offsetMax = new Vector2(0f, -118f);

            var panel = UIFactory.Panel(root.transform, "QuestsPanel");
            var prt = UIFactory.Rt(panel);
            prt.anchorMin = prt.anchorMax = new Vector2(0.5f, 0.5f);
            prt.pivot = new Vector2(0.5f, 0.5f);
            prt.sizeDelta = new Vector2(1180f, 452f);
            prt.anchoredPosition = new Vector2(0f, -6f);

            var fill = panel.transform.Find("Fill");
            var row = UIFactory.Child(fill, "Row");
            UIFactory.Stretch(UIFactory.Rt(row), 26f, 20f, 26f, 20f);
            var h = row.AddComponent<HorizontalLayoutGroup>();
            h.spacing = ArcadeTheme.Xl;
            h.childForceExpandWidth = false;
            h.childForceExpandHeight = true;
            h.childControlWidth = true;
            h.childControlHeight = true;

            BuildLevelColumn(row.transform);
            BuildQuestColumn(row.transform);

            BuildBack(root.transform);

            root.SetActive(false);
        }

        // ---------- left: the level ----------

        private void BuildLevelColumn(Transform parent)
        {
            var column = UIFactory.Child(parent, "LevelColumn");
            var le = column.AddComponent<LayoutElement>();
            le.preferredWidth = 300f;
            le.flexibleWidth = 0f;

            var v = column.AddComponent<VerticalLayoutGroup>();
            v.spacing = ArcadeTheme.Sm;
            v.childAlignment = TextAnchor.UpperCenter;
            v.childForceExpandWidth = true;
            v.childForceExpandHeight = false;
            v.childControlWidth = true;
            v.childControlHeight = true;

            var ringHolder = UIFactory.Child(column.transform, "Ring");
            ringHolder.AddComponent<LayoutElement>().preferredHeight = 140f;

            var ring = UIFactory.Child(ringHolder.transform, "RingArt");
            var art = UIFactory.Rt(ring);
            art.anchorMin = art.anchorMax = new Vector2(0.5f, 0.5f);
            art.pivot = new Vector2(0.5f, 0.5f);
            art.sizeDelta = new Vector2(136f, 136f);

            Ring(ring.transform, "Track", ArcadeTheme.Line, 0f);
            levelRing = Ring(ring.transform, "Fill", ArcadeTheme.Gold, 0f);
            levelRing.type = Image.Type.Filled;
            levelRing.fillMethod = Image.FillMethod.Radial360;
            levelRing.fillOrigin = (int)Image.Origin360.Top;
            levelRing.fillClockwise = true;
            Ring(ring.transform, "Hole", ArcadeTheme.BgPanel, 10f);

            levelNumber = UIFactory.Text(ring.transform, "1", ArcadeTheme.FsTitle * 1.05f,
                                         ArcadeTheme.Gold, display: true, bold: true);
            UIFactory.Stretch(UIFactory.Rt(levelNumber.gameObject), 0f);

            levelCaption = UIFactory.Text(column.transform, string.Empty, ArcadeTheme.FsCaption,
                                          ArcadeTheme.InkMuted, display: false, bold: true,
                                          upper: false, tracking: 2f);
            levelCaption.gameObject.AddComponent<LayoutElement>().preferredHeight = 22f;

            var track = UIFactory.Child(column.transform, "XpTrack");
            UIFactory.RoundedImage(track, ArcadeTheme.RadSm, ArcadeTheme.BgDeep, false);
            track.AddComponent<LayoutElement>().preferredHeight = 12f;

            var bar = UIFactory.Child(track.transform, "XpFill");
            xpFill = UIFactory.RoundedImage(bar, ArcadeTheme.RadSm, ArcadeTheme.Gold, false);
            xpFill.type = Image.Type.Filled;
            xpFill.fillMethod = Image.FillMethod.Horizontal;
            UIFactory.Stretch(UIFactory.Rt(bar), 1.5f);

            streakLabel = UIFactory.Text(column.transform, string.Empty, ArcadeTheme.FsCaption,
                                         ArcadeTheme.Gold, display: false, bold: true,
                                         upper: true, tracking: 6f);
            streakLabel.gameObject.AddComponent<LayoutElement>().preferredHeight = 24f;
        }

        /// <summary>
        /// A ring, faked as a disc with a smaller disc of the panel colour on top — the theme bakes
        /// no annulus, and <see cref="ArcadeTheme.SpinnerRing"/> is a comet rather than a meter.
        /// </summary>
        private static Image Ring(Transform parent, string name, Color color, float inset)
        {
            var go = UIFactory.Child(parent, name);
            var img = go.AddComponent<Image>();
            img.sprite = ArcadeTheme.Disc();
            img.type = Image.Type.Simple;
            img.color = color;
            img.raycastTarget = false;
            UIFactory.Stretch(UIFactory.Rt(go), inset);
            return img;
        }

        // ---------- right: the quests ----------

        private void BuildQuestColumn(Transform parent)
        {
            var column = UIFactory.Child(parent, "QuestColumn");
            column.AddComponent<LayoutElement>().flexibleWidth = 1f;

            var v = column.AddComponent<VerticalLayoutGroup>();
            v.spacing = ArcadeTheme.Sm;
            v.childForceExpandWidth = true;
            v.childForceExpandHeight = false;
            v.childControlWidth = true;
            v.childControlHeight = true;

            cardHost = UIFactory.Child(column.transform, "Cards").transform;
            var cv = cardHost.gameObject.AddComponent<VerticalLayoutGroup>();
            cv.spacing = ArcadeTheme.Sm;
            cv.childForceExpandWidth = true;
            cv.childForceExpandHeight = false;
            cv.childControlWidth = true;
            cv.childControlHeight = true;
            cardHost.gameObject.AddComponent<LayoutElement>().preferredHeight = 316f;

            BuildSlamStrip(column.transform);
        }

        private void BuildSlamStrip(Transform parent)
        {
            var strip = UIFactory.Child(parent, "Slam");
            strip.AddComponent<LayoutElement>().preferredHeight = 52f;
            UIFactory.RoundedImage(strip, ArcadeTheme.RadMd, ArcadeTheme.BgRaised, false);

            var icon = UIFactory.Child(strip.transform, "Icon");
            var irt = UIFactory.Rt(icon);
            irt.anchorMin = irt.anchorMax = new Vector2(0f, 0.5f);
            irt.pivot = new Vector2(0f, 0.5f);
            irt.sizeDelta = new Vector2(44f, 44f);
            irt.anchoredPosition = new Vector2(ArcadeTheme.Md, 0f);
            QuestIcons.Glyph(icon.transform, default, true, 30f, ArcadeTheme.BgRaised);

            slamNote = UIFactory.Text(strip.transform, string.Empty, ArcadeTheme.FsCaption,
                                      ArcadeTheme.InkMuted, display: false, bold: true,
                                      upper: true, tracking: 4f, align: TextAlignmentOptions.Left);
            UIFactory.Stretch(UIFactory.Rt(slamNote.gameObject), 66f, 0f, 190f, 0f);

            var pips = UIFactory.Child(strip.transform, "Pips");
            var prt = UIFactory.Rt(pips);
            prt.anchorMin = prt.anchorMax = new Vector2(1f, 0.5f);
            prt.pivot = new Vector2(1f, 0.5f);
            prt.sizeDelta = new Vector2(170f, 24f);
            prt.anchoredPosition = new Vector2(-ArcadeTheme.Md, 0f);

            var ph = pips.AddComponent<HorizontalLayoutGroup>();
            ph.spacing = ArcadeTheme.Sm;
            ph.childAlignment = TextAnchor.MiddleRight;
            ph.childForceExpandWidth = false;
            ph.childForceExpandHeight = false;
            ph.childControlWidth = true;
            ph.childControlHeight = true;
            slamPips = pips.transform;
        }

        /// <summary>
        /// One quest. The icon carries its own progress in the ring around it, so the bar underneath
        /// is a second reading of the same fact rather than the only one — which is what makes the
        /// card legible at a glance before any of it is read.
        /// </summary>
        private void BuildCard(int slot)
        {
            QuestDefinition definition = DailyQuests.DefinitionAt(slot);
            bool done = DailyQuests.IsCompleteAt(slot);
            int progress = DailyQuests.ProgressAt(slot);
            Color tier = QuestIcons.TierColor(definition.Tier);

            var card = UIFactory.Child(cardHost, "Quest_" + definition.Id);
            card.AddComponent<LayoutElement>().preferredHeight = 100f;
            UIFactory.RoundedImage(card, ArcadeTheme.RadMd,
                                   done ? ArcadeTheme.BgRaised : ArcadeTheme.BgPanel, false);

            var border = UIFactory.Child(card.transform, "Border");
            UIFactory.RoundedImage(border, ArcadeTheme.RadMd, done ? ArcadeTheme.Gold : ArcadeTheme.Line, false);
            UIFactory.Stretch(UIFactory.Rt(border), 0f);
            var inner = UIFactory.Child(card.transform, "Inner");
            UIFactory.RoundedImage(inner, ArcadeTheme.RadMd,
                                   done ? ArcadeTheme.BgRaised : ArcadeTheme.BgPanel, false);
            UIFactory.Stretch(UIFactory.Rt(inner), 1.5f);

            var iconHolder = UIFactory.Child(card.transform, "Icon");
            var irt = UIFactory.Rt(iconHolder);
            irt.anchorMin = irt.anchorMax = new Vector2(0f, 0.5f);
            irt.pivot = new Vector2(0f, 0.5f);
            irt.sizeDelta = new Vector2(76f, 76f);
            irt.anchoredPosition = new Vector2(ArcadeTheme.Md, 0f);
            QuestIcons.Disc(iconHolder.transform, definition.Id, definition.Tier,
                            DailyQuests.Progress01At(slot), done, false, 72f);

            var titleRow = UIFactory.Text(card.transform, definition.Title, ArcadeTheme.FsBody,
                                          done ? ArcadeTheme.Gold : ArcadeTheme.Ink,
                                          display: true, bold: true, upper: true, tracking: 2f,
                                          align: TextAlignmentOptions.Left);
            var tr = UIFactory.Rt(titleRow.gameObject);
            tr.anchorMin = new Vector2(0f, 1f);
            tr.anchorMax = new Vector2(1f, 1f);
            tr.pivot = new Vector2(0.5f, 1f);
            tr.offsetMin = new Vector2(104f, -36f);
            tr.offsetMax = new Vector2(-190f, -14f);

            var tierChip = UIFactory.Text(card.transform, definition.Tier.ToString(),
                                          ArcadeTheme.FsCaption * 0.8f, tier,
                                          display: false, bold: true, upper: true, tracking: 8f,
                                          align: TextAlignmentOptions.Right);
            var cr = UIFactory.Rt(tierChip.gameObject);
            cr.anchorMin = new Vector2(1f, 1f);
            cr.anchorMax = new Vector2(1f, 1f);
            cr.pivot = new Vector2(1f, 1f);
            cr.sizeDelta = new Vector2(180f, 22f);
            cr.anchoredPosition = new Vector2(-ArcadeTheme.Md, -14f);

            // The AI quest is the one exception to "online only", and the card has to say so — a
            // player who never goes online should be able to see which one they can still reach.
            string where = definition.OnlineOnly ? string.Empty : "  •  offline";
            var caption = UIFactory.Text(card.transform, definition.Description + where,
                                         ArcadeTheme.FsCaption, ArcadeTheme.InkMuted,
                                         display: false, bold: false, upper: false, tracking: 0f,
                                         align: TextAlignmentOptions.Left);
            var qr = UIFactory.Rt(caption.gameObject);
            qr.anchorMin = new Vector2(0f, 1f);
            qr.anchorMax = new Vector2(1f, 1f);
            qr.pivot = new Vector2(0.5f, 1f);
            qr.offsetMin = new Vector2(104f, -60f);
            qr.offsetMax = new Vector2(-104f, -38f);

            var track = UIFactory.Child(card.transform, "Track");
            UIFactory.RoundedImage(track, ArcadeTheme.RadSm, ArcadeTheme.BgDeep, false);
            var kr = UIFactory.Rt(track);
            kr.anchorMin = new Vector2(0f, 0f);
            kr.anchorMax = new Vector2(1f, 0f);
            kr.pivot = new Vector2(0.5f, 0f);
            kr.offsetMin = new Vector2(104f, 20f);
            kr.offsetMax = new Vector2(-180f, 30f);

            var barFill = UIFactory.Child(track.transform, "Fill");
            var bar = UIFactory.RoundedImage(barFill, ArcadeTheme.RadSm, done ? ArcadeTheme.Gold : tier, false);
            bar.type = Image.Type.Filled;
            bar.fillMethod = Image.FillMethod.Horizontal;
            bar.fillAmount = DailyQuests.Progress01At(slot);
            UIFactory.Stretch(UIFactory.Rt(barFill), 1.5f);

            var count = UIFactory.Text(card.transform, $"{progress}/{definition.Target}",
                                       ArcadeTheme.FsCaption, done ? ArcadeTheme.Gold : ArcadeTheme.Ink,
                                       display: true, bold: true, upper: true, tracking: 1f,
                                       align: TextAlignmentOptions.Left);
            var nr = UIFactory.Rt(count.gameObject);
            nr.anchorMin = new Vector2(1f, 0f);
            nr.anchorMax = new Vector2(1f, 0f);
            nr.pivot = new Vector2(1f, 0f);
            nr.sizeDelta = new Vector2(170f, 24f);
            nr.anchoredPosition = new Vector2(-ArcadeTheme.Md - 76f, 14f);

            var xp = UIFactory.Text(card.transform, "+" + definition.Xp, ArcadeTheme.FsBody,
                                    done ? ArcadeTheme.Gold : tier, display: true, bold: true,
                                    upper: true, tracking: 1f, align: TextAlignmentOptions.Right);
            var xr = UIFactory.Rt(xp.gameObject);
            xr.anchorMin = new Vector2(1f, 0f);
            xr.anchorMax = new Vector2(1f, 0f);
            xr.pivot = new Vector2(1f, 0f);
            xr.sizeDelta = new Vector2(90f, 30f);
            xr.anchoredPosition = new Vector2(-ArcadeTheme.Md, 14f);
        }

        private void BuildBack(Transform parent)
        {
            var holder = UIFactory.Child(parent, "BackHolder");
            var rt = UIFactory.Rt(holder);
            rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 0f);
            rt.pivot = new Vector2(0.5f, 0f);
            rt.sizeDelta = new Vector2(260f, 54f);
            // The same place the friends list and the account screen put theirs, so moving between
            // them does not move the way out.
            rt.anchoredPosition = new Vector2(0f, 44f);

            var layout = holder.AddComponent<VerticalLayoutGroup>();
            layout.childForceExpandWidth = true;
            layout.childForceExpandHeight = false;
            layout.childControlWidth = true;
            layout.childControlHeight = true;

            UIFactory.Button(holder.transform, "Back", MenuButton.Variant.Ghost,
                             () => OnBack?.Invoke(), 54f);
        }

        // ---------- state ----------

        public void Open()
        {
            if (root == null)
            {
                return;
            }

            DailyQuests.EnsureToday();

            // Opening the screen is what "seeing" a cleared quest means, so the dot on the main menu
            // goes out here rather than when the quest was banked — the player may well have banked
            // it on a result screen they walked straight past.
            DailyQuests.MarkSeen();

            DailyQuests.OnChanged -= Redraw;
            DailyQuests.OnChanged += Redraw;
            PlayerXp.OnChanged -= Redraw;
            PlayerXp.OnChanged += Redraw;

            Redraw();

            root.SetActive(true);
            root.transform.SetAsLastSibling();
            group.blocksRaycasts = true;
            group.alpha = 1f;

            StartCoroutine(UITween.Stagger(this, cardHost, ArcadeTheme.Stagger, ArcadeTheme.TNormal));
        }

        public void Close()
        {
            DailyQuests.OnChanged -= Redraw;
            PlayerXp.OnChanged -= Redraw;

            if (root != null)
            {
                group.blocksRaycasts = false;
                root.SetActive(false);
            }
        }

        private void OnDestroy()
        {
            DailyQuests.OnChanged -= Redraw;
            PlayerXp.OnChanged -= Redraw;
        }

        /// <summary>
        /// Keeps the countdown honest while the screen is up. Once a second, unscaled — the front end
        /// runs at <c>timeScale 0</c> and a scaled timer here would simply never move.
        /// </summary>
        private void Update()
        {
            if (root == null || !root.activeSelf || Time.unscaledTime < nextTick)
            {
                return;
            }

            nextTick = Time.unscaledTime + 1f;
            RefreshReset();
        }

        private void RefreshReset()
        {
            if (resetLabel == null)
            {
                return;
            }

            double seconds = DailyQuests.SecondsUntilReset;
            int hours = (int)(seconds / 3600d);
            int minutes = (int)((seconds % 3600d) / 60d);

            resetLabel.text = hours > 0
                ? $"new set in {hours}h {minutes}m"
                : $"new set in {minutes}m";
        }

        private void Redraw()
        {
            if (root == null)
            {
                return;
            }

            UIFactory.ClearChildren(cardHost);
            for (int slot = 0; slot < DailyQuests.Slots; slot++)
            {
                BuildCard(slot);
            }

            if (levelNumber != null) levelNumber.text = PlayerXp.Level.ToString();
            if (levelRing != null) levelRing.fillAmount = PlayerXp.Progress01;
            if (xpFill != null) xpFill.fillAmount = PlayerXp.Progress01;

            if (levelCaption != null)
            {
                int left = PlayerXp.LevelSpan - PlayerXp.IntoLevel;
                levelCaption.text = $"{left} XP to level {PlayerXp.Level + 1}";
            }

            if (streakLabel != null)
            {
                int streak = DailyQuests.DayStreak;
                streakLabel.text = streak > 0 ? $"day {streak} streak" : "no streak yet";
                streakLabel.color = streak > 0 ? ArcadeTheme.Gold : ArcadeTheme.InkMuted;
            }

            RedrawSlam();
            RefreshReset();
        }

        private void RedrawSlam()
        {
            if (slamPips == null)
            {
                return;
            }

            int done = DailyQuests.CompletedCount;

            UIFactory.ClearChildren(slamPips);
            for (int i = 0; i < DailyQuests.Slots; i++)
            {
                var pip = UIFactory.Child(slamPips, "Pip");
                var img = pip.AddComponent<Image>();
                img.sprite = ArcadeTheme.Disc();
                img.color = i < done ? ArcadeTheme.Gold : ArcadeTheme.Line;
                img.raycastTarget = false;
                var le = pip.AddComponent<LayoutElement>();
                le.preferredWidth = 14f;
                le.preferredHeight = 14f;
            }

            var xp = UIFactory.Text(slamPips, "+" + QuestDefinition.SlamXp, ArcadeTheme.FsBody,
                                    ArcadeTheme.Gold, display: true, bold: true, upper: true,
                                    tracking: 1f, align: TextAlignmentOptions.Right);
            var le2 = xp.gameObject.AddComponent<LayoutElement>();
            le2.preferredWidth = 84f;
            le2.preferredHeight = 28f;

            if (slamNote != null)
            {
                slamNote.text = DailyQuests.SlamAwarded
                    ? "daily slam — banked"
                    : "daily slam — clear all three";
                slamNote.color = DailyQuests.SlamAwarded ? ArcadeTheme.Gold : ArcadeTheme.InkMuted;
            }
        }
    }
}
