using UnityEngine;
using UnityEngine.UIElements;

namespace TableFootball.UI.Toolkit
{
    /// <summary>
    /// The two typefaces: Oxanium for display (titles, numbers, buttons) and Barlow for body text.
    /// Both from Google Fonts under the SIL Open Font License; the files and their licences live in
    /// <c>Resources/UI/Fonts</c>.
    ///
    /// Applied from code rather than from the stylesheet, by class: a text element carries one of
    /// <c>f-display</c>, <c>f-display-semi</c>, <c>f-body-medium</c> or <c>f-body-semi</c>, and
    /// <see cref="Apply"/> sets the matching font on it. Everything else inherits Barlow Regular from
    /// the panel root. Setting fonts in code means a missing file degrades to the default font with a
    /// warning instead of silently failing to parse a stylesheet.
    ///
    /// Each weight is its own file on purpose. UI Toolkit's bold is synthesised from the regular
    /// face, and a synthesised bold Oxanium looks nothing like the real one.
    /// </summary>
    public static class UiFonts
    {
        private static Font displayBold, displaySemi, body, bodyMedium, bodySemi;
        private static bool loaded;

        private static void Load()
        {
            if (loaded) return;
            loaded = true;
            displayBold = LoadFont("Oxanium-Bold");
            displaySemi = LoadFont("Oxanium-SemiBold");
            body = LoadFont("Barlow-Regular");
            bodyMedium = LoadFont("Barlow-Medium");
            bodySemi = LoadFont("Barlow-SemiBold");
        }

        private static Font LoadFont(string file)
        {
            var font = Resources.Load<Font>("UI/Fonts/" + file);
            if (font == null) Debug.LogWarning($"UiFonts: Resources/UI/Fonts/{file}.ttf is missing.");
            return font;
        }

        /// <summary>Sets the default body face on an element, for everything under it to inherit.</summary>
        public static void ApplyBody(VisualElement element)
        {
            Load();
            Set(element, body);
        }

        /// <summary>Gives every text element under <paramref name="tree"/> the font its class names.</summary>
        public static void Apply(VisualElement tree)
        {
            Load();
            tree.Query(className: "f-display").ForEach(e => Set(e, displayBold));
            tree.Query(className: "f-display-semi").ForEach(e => Set(e, displaySemi));
            tree.Query(className: "f-body-medium").ForEach(e => Set(e, bodyMedium));
            tree.Query(className: "f-body-semi").ForEach(e => Set(e, bodySemi));
        }

        private static void Set(VisualElement e, Font font)
        {
            if (font == null) return;
            e.style.unityFontDefinition = new StyleFontDefinition(FontDefinition.FromFont(font));
        }
    }
}
