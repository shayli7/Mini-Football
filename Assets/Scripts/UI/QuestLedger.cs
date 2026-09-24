using System.Collections;
using System.Collections.Generic;
using TableFootball.Progression;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace TableFootball.UI
{
    /// <summary>
    /// What the match just cleared, on the result screen.
    ///
    /// This is the whole payoff and the only place a quest ever announces itself. Nothing interrupts
    /// a live match: a card sliding over the table pulls the eye off the ball at exactly the moment
    /// the player earned the right to enjoy it, and on a phone it lands under a thumb. The reward
    /// waits for the whistle, where they are already looking and have both hands free.
    ///
    /// Built into <see cref="ScoreHud"/>'s banner as one more row of its layout group, and hidden
    /// entirely when nothing was cleared — an empty "QUESTS CLEARED" heading would be worse than no
    /// heading at all.
    /// </summary>
    [DisallowMultipleComponent]
    public class QuestLedger : MonoBehaviour
    {
        private const float RowHeight = 42f;
        private const float RowGap = 4f;
        private const float RuleHeight = 26f;
        private const float XpHeight = 44f;

        private GameObject root;
        private Transform rowHost;
        private TextMeshProUGUI levelLabel;
        private TextMeshProUGUI xpLabel;
        private Image barFill;

        private readonly List<Transform> rows = new List<Transform>();
        private int slamRow = -1;

        /// <summary>
        /// Raised as the slam row lands, so the owner can throw its burst behind it. Held here
        /// rather than owned here because the burst belongs to the banner this sits inside, and a
        /// second one of its own would fight the first for the same patch of screen.
        /// </summary>
        public System.Action OnSlam;

        /// <summary>How much height the banner has to find for the ledger. 0 when it is hidden.</summary>
        public float Height { get; private set; }

        /// <summary>Adds the ledger into a vertical layout group. Starts hidden.</summary>
        public void Build(Transform parent)
        {
            root = UIFactory.Child(parent, "QuestLedger");
            root.AddComponent<LayoutElement>().preferredHeight = 0f;

            var v = root.AddComponent<VerticalLayoutGroup>();
            v.spacing = ArcadeTheme.Xs;
            v.childAlignment = TextAnchor.UpperCenter;
            v.childForceExpandWidth = true;
            v.childForceExpandHeight = false;
            v.childControlWidth = true;
            v.childControlHeight = true;

            UIFactory.SectionRule(root.transform, "quests cleared");

            var host = UIFactory.Child(root.transform, "Rows");
            var rv = host.AddComponent<VerticalLayoutGroup>();
            rv.spacing = RowGap;
            rv.childAlignment = TextAnchor.UpperCenter;
            rv.childForceExpandWidth = true;
            rv.childForceExpandHeight = false;
            rv.childControlWidth = true;
            rv.childControlHeight = true;
            rowHost = host.transform;

            BuildXpBlock(root.transform);

            root.SetActive(false);
        }

        private void BuildXpBlock(Transform parent)
        {
            var block = UIFactory.Child(parent, "Xp");
            block.AddComponent<LayoutElement>().preferredHeight = XpHeight;

            var v = block.AddComponent<VerticalLayoutGroup>();
            v.spacing = ArcadeTheme.Xs;
            v.childForceExpandWidth = true;
            v.childForceExpandHeight = false;
            v.childControlWidth = true;
            v.childControlHeight = true;

            var head = UIFactory.Child(block.transform, "Head");
            head.AddComponent<LayoutElement>().preferredHeight = 20f;
            var h = head.AddComponent<HorizontalLayoutGroup>();
            h.childForceExpandWidth = true;
            h.childControlWidth = true;
            h.childControlHeight = true;

            levelLabel = UIFactory.Text(head.transform, "LEVEL 1", ArcadeTheme.FsCaption,
                                        ArcadeTheme.Ink, display: true, bold: true, upper: true,
                                        tracking: 6f, align: TextAlignmentOptions.Left);

            xpLabel = UIFactory.Text(head.transform, "+0 XP", ArcadeTheme.FsCaption,
                                     ArcadeTheme.Gold, display: false, bold: true, upper: true,
                                     tracking: 2f, align: TextAlignmentOptions.Right);

            var track = UIFactory.Child(block.transform, "Track");
            UIFactory.RoundedImage(track, ArcadeTheme.RadSm, ArcadeTheme.BgDeep, false);
            track.AddComponent<LayoutElement>().preferredHeight = 12f;

            var fill = UIFactory.Child(track.transform, "Fill");
            barFill = UIFactory.RoundedImage(fill, ArcadeTheme.RadSm, ArcadeTheme.Gold, false);
            barFill.type = Image.Type.Filled;
            barFill.fillMethod = Image.FillMethod.Horizontal;
            barFill.fillAmount = 0f;
            UIFactory.Stretch(UIFactory.Rt(fill), 1.5f);
        }

        // ---------- showing ----------

        /// <summary>
        /// Lays out the rows for a finished match and reports whether there is anything to show.
        /// Everything is built in its final state here; <see cref="Play"/> does the animating, so a
        /// reduced-motion player and an interrupted sequence both end up with a correct screen.
        /// </summary>
        public bool Populate(IReadOnlyList<QuestAward> awards)
        {
            if (root == null)
            {
                return false;
            }

            rows.Clear();
            slamRow = -1;
            UIFactory.ClearChildren(rowHost);

            if (awards == null || awards.Count == 0)
            {
                Height = 0f;
                root.GetComponent<LayoutElement>().preferredHeight = 0f;
                root.SetActive(false);
                return false;
            }

            foreach (QuestAward award in awards)
            {
                if (award.IsSlam)
                {
                    slamRow = rows.Count;
                }

                rows.Add(BuildRow(award));
            }

            Height = RuleHeight + awards.Count * RowHeight + (awards.Count - 1) * RowGap
                     + XpHeight + ArcadeTheme.Xs * 2f;

            root.GetComponent<LayoutElement>().preferredHeight = Height;
            root.SetActive(true);

            if (xpLabel != null) xpLabel.text = "+0 XP";
            if (levelLabel != null) levelLabel.text = $"LEVEL {PlayerXp.LevelOf(PlayerXp.Total - QuestTracker.LastXp)}";
            if (barFill != null) barFill.fillAmount = PlayerXp.Progress01Of(PlayerXp.Total - QuestTracker.LastXp);

            return true;
        }

        private Transform BuildRow(QuestAward award)
        {
            var row = UIFactory.Child(rowHost, "Row_" + award.Title);
            row.AddComponent<LayoutElement>().preferredHeight = RowHeight;

            bool slam = award.IsSlam;
            QuestDefinition definition = slam ? null : QuestDefinition.Get(award.Id);
            Color accent = slam ? ArcadeTheme.Gold : QuestIcons.TierColor(definition.Tier);

            UIFactory.RoundedImage(row, ArcadeTheme.RadMd,
                                   slam ? ArcadeTheme.Gold.WithAlpha(0.13f) : ArcadeTheme.BgRaised,
                                   false);

            // The tier down the leading edge, so a gold row is recognisable before it is read.
            var edge = UIFactory.Child(row.transform, "Edge");
            UIFactory.RoundedImage(edge, ArcadeTheme.RadSm, accent, false);
            var ert = UIFactory.Rt(edge);
            ert.anchorMin = new Vector2(0f, 0f);
            ert.anchorMax = new Vector2(0f, 1f);
            ert.pivot = new Vector2(0f, 0.5f);
            ert.offsetMin = new Vector2(0f, 6f);
            ert.offsetMax = new Vector2(3f, -6f);

            var icon = UIFactory.Child(row.transform, "Icon");
            var irt = UIFactory.Rt(icon);
            irt.anchorMin = new Vector2(0f, 0.5f);
            irt.anchorMax = new Vector2(0f, 0.5f);
            irt.pivot = new Vector2(0f, 0.5f);
            irt.sizeDelta = new Vector2(RowHeight, RowHeight);
            irt.anchoredPosition = new Vector2(ArcadeTheme.Sm, 0f);
            QuestIcons.Glyph(icon.transform, award.Id, slam, 30f, ArcadeTheme.BgRaised);

            var title = UIFactory.Text(row.transform, award.Title, ArcadeTheme.FsBody * 0.86f,
                                       slam ? ArcadeTheme.Gold : ArcadeTheme.Ink,
                                       display: true, bold: true, upper: true, tracking: 2f,
                                       align: TextAlignmentOptions.Left);
            UIFactory.Stretch(UIFactory.Rt(title.gameObject), RowHeight + ArcadeTheme.Md, 0f, 76f, 0f);

            var xp = UIFactory.Text(row.transform, "+" + award.Xp, ArcadeTheme.FsBody * 0.86f,
                                    ArcadeTheme.Gold, display: true, bold: true, upper: true,
                                    tracking: 1f, align: TextAlignmentOptions.Right);
            UIFactory.Stretch(UIFactory.Rt(xp.gameObject), 0f, 0f, ArcadeTheme.Md, 0f);

            return row.transform;
        }

        /// <summary>
        /// Runs the sequence: each row lands in turn with its own note, then the bar catches up.
        ///
        /// Unscaled throughout, like every other front-end animation here — the result screen sits at
        /// <c>timeScale 0</c>, so a row tweened on scaled time would never animate at all.
        /// </summary>
        public IEnumerator Play()
        {
            if (root == null || !root.activeSelf)
            {
                yield break;
            }

            for (int i = 0; i < rows.Count; i++)
            {
                if (rows[i] == null) continue;

                StartCoroutine(UITween.PopIn(rows[i], ArcadeTheme.TNormal));
                GameSfx.PlayQuestComplete(i);

                if (i == slamRow)
                {
                    OnSlam?.Invoke();
                }

                if (!ArcadeTheme.ReducedMotion && i < rows.Count - 1)
                {
                    yield return new WaitForSecondsRealtime(ArcadeTheme.Stagger * 3f);
                }
            }

            if (!ArcadeTheme.ReducedMotion)
            {
                yield return new WaitForSecondsRealtime(ArcadeTheme.TNormal);
            }

            yield return RunBar();
        }

        /// <summary>
        /// Fills the bar from where the player was to where they are, wrapping once per level gained.
        ///
        /// Wrapping rather than jumping is the point: a bar that simply reappeared lower down would
        /// read as having gone backwards.
        /// </summary>
        private IEnumerator RunBar()
        {
            int gained = QuestTracker.LastXp;
            int before = PlayerXp.Total - gained;

            var counting = StartCoroutine(UITween.CountUp(xpLabel, 0, gained, ArcadeTheme.TSlow,
                                                          GameSfx.PlayUiClick));

            int levels = QuestTracker.LastLevelsGained;
            float from = PlayerXp.Progress01Of(before);

            for (int lap = 0; lap < levels; lap++)
            {
                yield return Sweep(from, 1f, ArcadeTheme.TNormal);
                if (levelLabel != null) levelLabel.text = $"LEVEL {PlayerXp.LevelOf(before) + lap + 1}";
                from = 0f;
                if (barFill != null) barFill.fillAmount = 0f;
            }

            yield return Sweep(from, PlayerXp.Progress01, ArcadeTheme.TSlow);
            yield return counting;

            if (xpLabel != null) xpLabel.text = $"+{gained} XP";
            if (levelLabel != null) levelLabel.text = $"LEVEL {PlayerXp.Level}";
        }

        private IEnumerator Sweep(float from, float to, float duration)
        {
            if (barFill == null) yield break;

            if (ArcadeTheme.ReducedMotion || duration <= 0f)
            {
                barFill.fillAmount = to;
                yield break;
            }

            float e = 0f;
            while (e < duration)
            {
                e += Time.unscaledDeltaTime;
                barFill.fillAmount = Mathf.Lerp(from, to, ArcadeTheme.EaseOut(Mathf.Clamp01(e / duration)));
                yield return null;
            }

            barFill.fillAmount = to;
        }

        public void Hide()
        {
            Height = 0f;
            if (root == null) return;
            root.GetComponent<LayoutElement>().preferredHeight = 0f;
            root.SetActive(false);
        }
    }
}
