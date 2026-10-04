using System;
using TableFootball.UI.Toolkit;
using UnityEngine;
using UnityEngine.UIElements;

namespace TableFootball.UI
{
    /// <summary>
    /// The one place the game tells the player something went wrong. Two forms, both on UI Toolkit
    /// (see <see cref="UiToolkitHost"/>) and styled by <c>Resources/UI/Styles/ErrorScreen.uss</c>:
    ///
    /// <see cref="Show"/> is a full-screen stop with RETRY and BACK TO MENU, for a failure the player
    /// cannot carry on past. <see cref="ShowToast"/> is a small banner that never takes the pointer,
    /// for one they can.
    ///
    /// Callers pass the message — <c>OnlineSession.LastError</c> and friends already hold one — and
    /// decide what RETRY and BACK do; like every screen, this one sequences nothing itself.
    /// </summary>
    public class ErrorScreen : MonoBehaviour
    {
        public enum ErrorKind { Generic, Offline, Connection }

        private VisualElement root;
        private VisualElement iconHost;
        private Label title;
        private Label message;
        private VisualElement buttons;
        private VisualElement toast;
        private Label toastText;
        private IVisualElementScheduledItem toastHide;

        public bool IsVisible => root != null && root.style.display != DisplayStyle.None;

        public void Build()
        {
            root = UiKit.El("screen error", UiToolkitHost.Root, "ErrorScreen");
            var sheet = Resources.Load<StyleSheet>("UI/Styles/ErrorScreen");
            if (sheet != null) root.styleSheets.Add(sheet);
            UiBackdrop.AddTo(root);

            var panel = UiKit.Frame("frame frame--panel error__panel", root);
            iconHost = UiKit.El("error__icon", panel);
            iconHost.pickingMode = PickingMode.Ignore;
            title = UiKit.Text(string.Empty, "error__title f-display", panel);
            message = UiKit.Text(string.Empty, "error__message f-body-medium", panel);
            buttons = UiKit.El("error__buttons", panel);

            toast = UiKit.Frame("frame error__toast", UiToolkitHost.Root, "ErrorToast");
            toast.pickingMode = PickingMode.Ignore;
            var toastIcon = new UiIcon(UiIcon.Glyph.Info, ArcadeTheme.Danger) { pickingMode = PickingMode.Ignore };
            toastIcon.AddToClassList("error__toast-icon");
            toast.Add(toastIcon);
            toastText = UiKit.Text(string.Empty, "error__toast-text f-body-semi", toast);

            UiFonts.Apply(root);
            UiFonts.Apply(toast);
            Hide();
            UiKit.Show(toast, false);
        }

        // ---------- full screen ----------

        /// <summary>
        /// Raises the error screen. <paramref name="onRetry"/> may be null, which leaves only BACK TO
        /// MENU. Both actions run after the screen has closed itself.
        /// </summary>
        public void Show(ErrorKind kind, string detail, Action onRetry, Action onBack)
        {
            if (root == null) return;

            title.text = TitleFor(kind);
            message.text = string.IsNullOrEmpty(detail) ? DefaultMessage(kind) : detail;

            iconHost.Clear();
            var glyph = kind == ErrorKind.Offline ? UiIcon.Glyph.WifiOff
                      : kind == ErrorKind.Connection ? UiIcon.Glyph.Close
                      : UiIcon.Glyph.Info;
            iconHost.Add(new UiIcon(glyph, ArcadeTheme.Danger) { pickingMode = PickingMode.Ignore });

            buttons.Clear();
            if (onRetry != null)
            {
                UiKit.Button("RETRY", "pulse", () => { Hide(); onRetry(); }, buttons);
            }
            UiKit.Button("BACK TO MENU", "ghost", () => { Hide(); onBack?.Invoke(); }, buttons);

            UiFonts.Apply(root);
            UiKit.Show(root, true);
            root.pickingMode = PickingMode.Position;
            root.BringToFront();
            GameSfx.PlayUi(UiSound.Warn);
            UiKit.Enter(root.Q(className: "error__panel"));
        }

        public void Hide()
        {
            if (root == null) return;
            // A hidden screen must not take the pointer, or every rod goes dead behind it.
            root.pickingMode = PickingMode.Ignore;
            UiKit.Show(root, false);
        }

        // ---------- toast ----------

        /// <summary>A short banner over whatever is on screen. Replaces one already showing.</summary>
        public void ShowToast(string text)
        {
            if (toast == null || string.IsNullOrEmpty(text)) return;

            toastText.text = text;
            UiKit.Show(toast, true);
            toast.BringToFront();
            GameSfx.PlayUi(UiSound.Deny);
            UiKit.Enter(toast);

            toastHide?.Pause();
            toastHide = toast.schedule.Execute(() => UiKit.Show(toast, false))
                                       .StartingIn((long)(ArcadeTheme.TToast * 1000f));
        }

        // ---------- copy ----------

        private static string TitleFor(ErrorKind kind)
        {
            switch (kind)
            {
                case ErrorKind.Offline: return "NO CONNECTION";
                case ErrorKind.Connection: return "CONNECTION LOST";
                default: return "SOMETHING WENT WRONG";
            }
        }

        private static string DefaultMessage(ErrorKind kind)
        {
            switch (kind)
            {
                case ErrorKind.Offline: return "Check your signal and try again. Local matches still work.";
                case ErrorKind.Connection: return "The link to the other player dropped.";
                default: return "Please try again.";
            }
        }
    }
}
