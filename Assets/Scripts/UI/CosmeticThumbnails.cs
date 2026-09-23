using System.Collections.Generic;
using UnityEngine;

namespace TableFootball.UI
{
    /// <summary>
    /// The rendered preview picture for a cosmetic, used by the store, the level path and the reward
    /// reveal.
    ///
    /// These are real renders of the real thing — the actual ball, the actual pitch with its lines,
    /// the actual figures in red and blue — produced by <c>Art/Blender/make_thumbnails.py</c>. They
    /// exist because a skin's own texture is NOT a picture of it: the ball and figure maps are uv
    /// layouts, and a soccer ball's equirectangular map shown raw reads as a stretched blob.
    ///
    /// They are transparent PNGs, so whatever draws them keeps its own rarity-tinted backdrop
    /// showing through. Not every cosmetic has one — the table skins are still placeholders and the
    /// league badges are drawn procedurally — and a miss is the normal answer, not an error:
    /// <see cref="UIFactory.CosmeticSwatch"/> falls back to its generated swatch.
    /// </summary>
    public static class CosmeticThumbnails
    {
        private const string Folder = "SkinThumbs/";

        private static readonly Dictionary<string, Sprite> Cache = new();

        /// <summary>Ids already known to have no thumbnail. Remembered so a card for a cosmetic
        /// without art does not hit <see cref="Resources.Load"/> again on every redraw — the store
        /// rebuilds its whole grid on any wallet or inventory change.</summary>
        private static readonly HashSet<string> Missing = new();

        /// <summary>The preview for a cosmetic id, or null when there is none.</summary>
        public static Sprite For(string id)
        {
            if (string.IsNullOrEmpty(id) || Missing.Contains(id))
            {
                return null;
            }

            if (Cache.TryGetValue(id, out Sprite cached) && cached != null)
            {
                return cached;
            }

            // Ids carry dots ("ball.soccer"); filenames cannot rely on them surviving every tool, so
            // the renders are written with underscores and the mapping is done here.
            var tex = Resources.Load<Texture2D>(Folder + "Thumb_" + id.Replace('.', '_'));
            if (tex == null)
            {
                Missing.Add(id);
                return null;
            }

            // Built at runtime rather than importing 25 textures as Sprite assets by hand. Sprite
            // .Create needs no CPU-readable texture, so this costs nothing but the reference.
            var sprite = Sprite.Create(tex, new Rect(0f, 0f, tex.width, tex.height),
                                       new Vector2(0.5f, 0.5f), 100f);
            Cache[id] = sprite;
            return sprite;
        }

        public static bool Has(string id) => For(id) != null;
    }
}
