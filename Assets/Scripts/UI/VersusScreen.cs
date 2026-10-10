using System;
using TableFootball.Net;
using TableFootball.UI.Toolkit;
using UnityEngine;
using UnityEngine.UIElements;

namespace TableFootball.UI
{
    /// <summary>
    /// The two-second face-off before an online match: the screen split diagonally into Red and Blue,
    /// each player's avatar, name, level and worn badge on their own team's side, and VS on the seam.
    ///
    /// Red is always on the left and Blue on the right, so a player finds themselves by their colour —
    /// the same colour their figures wear on the table the moment this lifts.
    ///
    /// On UI Toolkit (see <see cref="UiToolkitHost"/>), styled by <c>Resources/UI/Styles/Versus.uss</c>.
    /// It only shows what it is given and closes itself after the duration it is handed; the match's
    /// timing belongs to <c>OnlineMatchDirector</c>, which holds the kick-off for the same length.
    /// While up it takes the pointer, so no rod is grabbed through it.
    /// </summary>
    public class VersusScreen : MonoBehaviour
    {
        /// <summary>Time the halves take to slide in, and the fade out at the end, in seconds.</summary>
        private const float SlideSeconds = 0.3f;
        private const float FadeOutSeconds = 0.25f;

        /// <summary>When VS lands and how long its stamp takes, in seconds after the screen opens.</summary>
        private const float VsDelay = 0.22f;
        private const float VsSeconds = 0.25f;

        /// <summary>Where the seam crosses the top and bottom edges, as a fraction of the width.</summary>
        private const float SeamTop = 0.58f;
        private const float SeamBottom = 0.42f;

        private VisualElement root;
        private DiagonalHalf redHalf, blueHalf;
        private Label vs;
        private IVisualElementScheduledItem ticker;
        private Action onDone;
        private float openedAt;
        private float duration;

        public bool IsOpen => root != null && UiKit.IsShown(root);

        public void Build()
        {
            root = UiKit.El("screen versus", UiToolkitHost.Root, "VersusScreen");
            var sheet = Resources.Load<StyleSheet>("UI/Styles/Versus");
            if (sheet != null) root.styleSheets.Add(sheet);
            else Debug.LogWarning("VersusScreen: Resources/UI/Styles/Versus.uss is missing.");

            redHalf = new DiagonalHalf(left: true, ArcadeTheme.Red);
            blueHalf = new DiagonalHalf(left: false, ArcadeTheme.Blue);
            root.Add(redHalf);
            root.Add(blueHalf);
            FitToScreenEdge(redHalf);
            FitToScreenEdge(blueHalf);

            vs = UiKit.Text("VS", "versus__vs f-display", root);
            vs.pickingMode = PickingMode.Ignore;

            Close();
        }

        /// <summary>
        /// Shows the two players for <paramref name="seconds"/>, then closes and runs
        /// <paramref name="done"/>. Opening again while open starts it over with the new cards.
        /// </summary>
        public void Show(PlayerCard red, PlayerCard blue, float seconds, Action done)
        {
            if (root == null)
            {
                done?.Invoke();
                return;
            }

            Fill(redHalf, red, ArcadeTheme.Red, "versus__card--red");
            Fill(blueHalf, blue, ArcadeTheme.Blue, "versus__card--blue");
            UiFonts.Apply(root);

            onDone = done;
            duration = Mathf.Max(seconds, SlideSeconds + FadeOutSeconds);
            openedAt = Time.unscaledTime;

            root.style.opacity = 1f;
            UiKit.Show(root, true);
            root.pickingMode = PickingMode.Position;
            root.BringToFront();
            GameSfx.PlayUi(UiSound.Open);

            Tick(Time.unscaledTime);
            ticker?.Pause();
            ticker = root.schedule.Execute(() => Tick(Time.unscaledTime)).Every(16);
        }

        /// <summary>Takes the screen down at once, without running the callback. Safe to call when closed.</summary>
        public void Close()
        {
            ticker?.Pause();
            ticker = null;
            onDone = null;

            if (root == null) return;

            // A hidden screen must not take the pointer, or every rod goes dead behind it.
            root.pickingMode = PickingMode.Ignore;
            UiKit.Show(root, false);
        }

        private void Tick(float now)
        {
            float t = now - openedAt;

            if (t >= duration)
            {
                Action done = onDone;
                Close();
                done?.Invoke();
                return;
            }

            bool still = ArcadeTheme.ReducedMotion;

            // The halves slide in from their own edges and meet at the seam.
            float slide = still ? 1f : ArcadeTheme.EaseOut(Mathf.Clamp01(t / SlideSeconds));
            float width = root.resolvedStyle.width;
            if (float.IsNaN(width)) width = UiToolkitHost.ReferenceWidth;
            redHalf.style.translate = new Translate(-(1f - slide) * width, 0f);
            blueHalf.style.translate = new Translate((1f - slide) * width, 0f);

            // VS stamps down once the halves have met.
            float stamp = still ? 1f : Mathf.Clamp01((t - VsDelay) / VsSeconds);
            vs.style.opacity = still ? 1f : ArcadeTheme.EaseOut(stamp);
            float scale = still ? 1f : Mathf.LerpUnclamped(1.8f, 1f, ArcadeTheme.EaseOutBack(stamp));
            vs.style.scale = new Scale(new Vector3(scale, scale, 1f));

            // The whole screen fades out in its last moment, onto the table it was hiding.
            float fadeFrom = duration - FadeOutSeconds;
            root.style.opacity = t < fadeFrom ? 1f : 1f - ArcadeTheme.EaseOut(Mathf.Clamp01((t - fadeFrom) / FadeOutSeconds));
        }

        /// <summary>Rebuilds one side's card: hex avatar with the initial, name with the badge beside
        /// it, and the level.</summary>
        private static void Fill(DiagonalHalf half, PlayerCard card, Color team, string sideClass)
        {
            half.Card.Clear();
            half.Card.ClearClassList();
            half.Card.AddToClassList("versus__card");
            half.Card.AddToClassList(sideClass);

            string name = card.Name.ToString();
            if (string.IsNullOrEmpty(name)) name = "Player";

            var hex = new UiHex { Edge = team, Fill = ArcadeTheme.Plate };
            hex.AddToClassList("versus__hex");
            half.Card.Add(hex);
            // Two UTF-16 units when the first character is a surrogate pair, so it is never split.
            int firstLength = char.IsHighSurrogate(name[0]) && name.Length > 1 ? 2 : 1;
            var initial = UiKit.Text(name.Substring(0, firstLength).ToUpperInvariant(), "versus__initial f-display", hex);
            initial.style.color = team;

            var nameRow = UiKit.El("versus__name-row", half.Card);
            var nameLabel = UiKit.Text(name, "versus__name f-display", nameRow);

            // The name came from the other player's machine: never let it be read as markup.
            nameLabel.enableRichText = false;

            if (card.HasBadge)
            {
                var badge = UiArt.LeagueBadge((League)card.Badge, 46f);
                badge.AddToClassList("versus__badge");
                nameRow.Add(badge);
            }

            UiKit.Text("LV " + Mathf.Max(1, card.Level), "versus__level f-display", half.Card);
        }

        /// <summary>Stretches a half past the safe area to the true screen edge, so the colour fills
        /// the whole display — the same fit the Arena backdrop uses.</summary>
        private static void FitToScreenEdge(VisualElement e)
        {
            void Fit()
            {
                Vector4 inset = UiToolkitHost.SafeInsets; // left, top, right, bottom
                e.style.left = -inset.x;
                e.style.top = -inset.y;
                e.style.right = -inset.z;
                e.style.bottom = -inset.w;
            }

            e.RegisterCallback<AttachToPanelEvent>(_ =>
            {
                UiToolkitHost.SafeAreaChanged += Fit;
                Fit();
            });
            e.RegisterCallback<DetachFromPanelEvent>(_ => UiToolkitHost.SafeAreaChanged -= Fit);
        }

        /// <summary>
        /// One team's side of the diagonal: a deep team-coloured field with a bright edge along the
        /// seam, drawn in code because USS cannot draw a slanted edge. Carries that team's card, which
        /// slides in with it.
        /// </summary>
        private sealed class DiagonalHalf : VisualElement
        {
            /// <summary>How much of the team colour shows in the field; the rest is the dark floor, so
            /// white text stays readable on both sides.</summary>
            private const float FieldStrength = 0.42f;

            /// <summary>Width of the bright seam edge, in panel units.</summary>
            private const float EdgeWidth = 5f;

            private readonly bool left;
            private readonly Color field;
            private readonly Color edge;

            public readonly VisualElement Card;

            public DiagonalHalf(bool left, Color team)
            {
                this.left = left;
                field = Color.Lerp(ArcadeTheme.BgDeep, team, FieldStrength);
                edge = team;

                AddToClassList("versus__half");
                pickingMode = PickingMode.Ignore;
                generateVisualContent += Draw;
                RegisterCallback<GeometryChangedEvent>(_ => MarkDirtyRepaint());

                Card = UiKit.El("versus__card", this);
                Card.pickingMode = PickingMode.Ignore;
            }

            private void Draw(MeshGenerationContext ctx)
            {
                float w = resolvedStyle.width, h = resolvedStyle.height;
                if (float.IsNaN(w) || float.IsNaN(h) || w <= 0f || h <= 0f) return;

                var p = ctx.painter2D;
                float top = w * SeamTop, bottom = w * SeamBottom;

                p.fillColor = field;
                p.BeginPath();
                if (left)
                {
                    p.MoveTo(new Vector2(0f, 0f));
                    p.LineTo(new Vector2(top, 0f));
                    p.LineTo(new Vector2(bottom, h));
                    p.LineTo(new Vector2(0f, h));
                }
                else
                {
                    p.MoveTo(new Vector2(top, 0f));
                    p.LineTo(new Vector2(w, 0f));
                    p.LineTo(new Vector2(w, h));
                    p.LineTo(new Vector2(bottom, h));
                }
                p.ClosePath();
                p.Fill();

                // The bright edge sits on this half's own side of the seam, so the two edges meet in
                // the middle as one red-and-blue line.
                float offset = left ? -EdgeWidth * 0.5f : EdgeWidth * 0.5f;
                p.strokeColor = edge;
                p.lineWidth = EdgeWidth;
                p.BeginPath();
                p.MoveTo(new Vector2(top + offset, 0f));
                p.LineTo(new Vector2(bottom + offset, h));
                p.Stroke();
            }
        }
    }
}
