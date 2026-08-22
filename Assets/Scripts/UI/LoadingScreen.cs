using System;
using System.Collections;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace TableFootball.UI
{
    /// <summary>
    /// Full-screen loading curtain with a progress bar along the bottom: a white outline filling
    /// with yellow.
    ///
    /// Used twice — once at boot, and again whenever play returns to the main menu — so it is a
    /// reusable curtain rather than a boot splash. That second use is the one that matters: it hides
    /// the table being torn down and rebuilt between matches.
    ///
    /// The fill is driven by <see cref="Progress"/> rather than directly by the timer, so real
    /// asynchronous work can drive it later without changing anything here. <see cref="Show"/>
    /// simply animates that value.
    ///
    /// Everything runs on unscaled time: the curtain is shown precisely when the world is frozen at
    /// timeScale 0, and would otherwise sit motionless forever.
    /// </summary>
    public class LoadingScreen : MonoBehaviour
    {
        private const float BarHeight = 26f;
        private const float BarInset = 64f;
        private const float BarBottom = 56f;
        private const float OutlineThickness = 3f;

        private GameObject root;
        private CanvasGroup group;
        private Image fill;
        private TextMeshProUGUI caption;
        private Coroutine running;

        private float progress;

        /// <summary>0–1, drives the bar. Set this directly to report real loading progress.</summary>
        public float Progress
        {
            get => progress;
            set
            {
                progress = Mathf.Clamp01(value);
                if (fill != null)
                {
                    fill.fillAmount = progress;
                }
            }
        }

        public bool IsVisible => root != null && root.activeSelf;

        public void Build(Transform canvasRoot)
        {
            root = UIFactory.Child(canvasRoot, "LoadingScreen");
            UIFactory.Stretch(UIFactory.Rt(root));

            group = root.AddComponent<CanvasGroup>();

            // Opaque and raycast-blocking: this is a curtain, and nothing behind it should be
            // clickable while it is up.
            var backdrop = UIFactory.Child(root.transform, "Backdrop");
            UIFactory.RoundedImage(backdrop, ArcadeTheme.RadSm, ArcadeTheme.BgDeep, true);
            UIFactory.Stretch(UIFactory.Rt(backdrop), -ArcadeTheme.Bleed);

            BuildTitle();
            BuildBar();

            Progress = 0f;
            root.SetActive(false);
        }

        private void BuildTitle()
        {
            var title = UIFactory.Text(root.transform, ArcadeTheme.GameName, ArcadeTheme.FsTitle,
                                       ArcadeTheme.Ink, display: true, bold: true, upper: true,
                                       tracking: 8f);
            var rt = UIFactory.Rt(title.gameObject);
            rt.anchorMin = new Vector2(0.5f, 0.5f);
            rt.anchorMax = new Vector2(0.5f, 0.5f);
            rt.pivot = new Vector2(0.5f, 0.5f);
            rt.sizeDelta = new Vector2(900f, 120f);
            rt.anchoredPosition = Vector2.zero;
        }

        private void BuildBar()
        {
            // Outline sits underneath; the track is inset inside it, so the outline reads as a
            // border without needing a second sprite or an Outline component.
            var outline = UIFactory.Child(root.transform, "BarOutline");
            UIFactory.RoundedImage(outline, ArcadeTheme.RadSm, ArcadeTheme.Ink, false);
            AnchorBar(UIFactory.Rt(outline), 0f);

            var track = UIFactory.Child(root.transform, "BarTrack");
            UIFactory.RoundedImage(track, ArcadeTheme.RadSm, ArcadeTheme.BgDeep, false);
            AnchorBar(UIFactory.Rt(track), OutlineThickness);

            var fillGo = UIFactory.Child(root.transform, "BarFill");
            fill = UIFactory.RoundedImage(fillGo, ArcadeTheme.RadSm, ArcadeTheme.Gold, false);
            fill.type = Image.Type.Filled;
            fill.fillMethod = Image.FillMethod.Horizontal;
            fill.fillOrigin = (int)Image.OriginHorizontal.Left;
            fill.fillAmount = 0f;
            AnchorBar(UIFactory.Rt(fillGo), OutlineThickness);

            caption = UIFactory.Text(root.transform, "LOADING", ArcadeTheme.FsCaption,
                                     ArcadeTheme.InkMuted, bold: true, upper: true, tracking: 6f);
            var crt = UIFactory.Rt(caption.gameObject);
            crt.anchorMin = new Vector2(0f, 0f);
            crt.anchorMax = new Vector2(1f, 0f);
            crt.pivot = new Vector2(0.5f, 0f);
            crt.offsetMin = new Vector2(BarInset, BarBottom + BarHeight + 10f);
            crt.offsetMax = new Vector2(-BarInset, BarBottom + BarHeight + 34f);
        }

        /// <summary>Pins a rect to the bottom strip, optionally inset to reveal the outline behind.</summary>
        private static void AnchorBar(RectTransform rt, float inset)
        {
            rt.anchorMin = new Vector2(0f, 0f);
            rt.anchorMax = new Vector2(1f, 0f);
            rt.pivot = new Vector2(0.5f, 0f);
            rt.offsetMin = new Vector2(BarInset + inset, BarBottom + inset);
            rt.offsetMax = new Vector2(-(BarInset + inset), BarBottom + BarHeight - inset);
        }

        /// <summary>
        /// Shows the curtain, fills the bar over <paramref name="seconds"/>, then fades out and
        /// calls <paramref name="onComplete"/>. The callback fires as the curtain clears, so the
        /// caller can swap screens underneath while it is still opaque.
        /// </summary>
        public void Show(float seconds, Action onComplete = null)
        {
            if (running != null)
            {
                StopCoroutine(running);
            }

            running = StartCoroutine(Run(Mathf.Max(seconds, 0.01f), onComplete));
        }

        public void Hide()
        {
            if (running != null)
            {
                StopCoroutine(running);
                running = null;
            }

            if (root != null)
            {
                root.SetActive(false);
            }
        }

        private IEnumerator Run(float seconds, Action onComplete)
        {
            root.SetActive(true);
            root.transform.SetAsLastSibling(); // above the menu and HUD
            group.alpha = 1f;
            group.blocksRaycasts = true;
            Progress = 0f;

            if (caption != null)
            {
                caption.text = "LOADING";
            }

            float elapsed = 0f;
            while (elapsed < seconds)
            {
                elapsed += Time.unscaledDeltaTime;
                Progress = ArcadeTheme.EaseOut(Mathf.Clamp01(elapsed / seconds));
                yield return null;
            }

            Progress = 1f;

            // A beat on a full bar, so it does not vanish the instant it completes.
            yield return new WaitForSecondsRealtime(0.15f);

            // Hand control back while still opaque: whatever comes next can build itself unseen.
            onComplete?.Invoke();

            yield return UITween.Fade(group, 1f, 0f, ArcadeTheme.TSlow, ArcadeTheme.EaseIn);

            group.blocksRaycasts = false;
            root.SetActive(false);
            running = null;
        }
    }
}
