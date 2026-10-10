using TableFootball.Net;
using UnityEngine;
using UnityEngine.UIElements;

namespace TableFootball.UI.Toolkit
{
    /// <summary>
    /// A gold coin: a bright face inside a darker rim. The one coin drawing for the UI Toolkit screens —
    /// prices, balances, level rewards — so the currency is one object the player learns once.
    /// </summary>
    public class UiCoin : VisualElement
    {
        public UiCoin()
        {
            pickingMode = PickingMode.Ignore;
            AddToClassList("coin");
            generateVisualContent += Draw;
            RegisterCallback<GeometryChangedEvent>(_ => MarkDirtyRepaint());
        }

        private void Draw(MeshGenerationContext ctx)
        {
            float w = resolvedStyle.width, h = resolvedStyle.height;
            if (float.IsNaN(w) || float.IsNaN(h) || w <= 0f || h <= 0f) return;

            var p = ctx.painter2D;
            var centre = new Vector2(w * 0.5f, h * 0.5f);
            float radius = Mathf.Min(w, h) * 0.5f;

            p.fillColor = ArcadeTheme.CoinDark;
            p.BeginPath();
            p.Arc(centre, radius, Angle.Degrees(0f), Angle.Degrees(360f));
            p.ClosePath();
            p.Fill();

            p.fillColor = ArcadeTheme.Coin;
            p.BeginPath();
            p.Arc(centre, radius * 0.72f, Angle.Degrees(0f), Angle.Degrees(360f));
            p.ClosePath();
            p.Fill();
        }
    }

    /// <summary>
    /// The Lucky Chest: a domed lid over a banded body with rivets and a shield lock, drawn in a tier's
    /// colour with the vector API so it stays crisp at any size. A still picture on its own; the chest that
    /// opens (<c>ChestOpening</c>) animates two of these, one drawing only the lid and one only the body, so
    /// the store and the opening show the very same chest.
    ///
    /// Drawn in a 200 x 200 design grid and scaled to fit whatever square the element is given, so size
    /// it from the stylesheet (<c>width</c> and <c>height</c>) and set <see cref="Tint"/> in code.
    /// </summary>
    public class UiChest : VisualElement
    {
        /// <summary>Which piece of the chest an element draws. The opening animation uses the lid and the body
        /// separately so the lid can fly off on its own.</summary>
        public enum Part { Whole, Lid, Body }

        private Color tint;
        private readonly Part part;

        /// <summary>A piece of the chest fills its element: it is scaled so the chest itself, not the square
        /// it is drawn in, spans the box, and lid and body line up when given the same box. The opening
        /// sizes them to a fixed box.</summary>
        public UiChest(Color tint, Part part = Part.Whole)
        {
            this.part = part;
            this.tint = tint;
            pickingMode = PickingMode.Ignore;
            AddToClassList("chest");
            generateVisualContent += Draw;
            RegisterCallback<GeometryChangedEvent>(_ => MarkDirtyRepaint());
        }

        /// <summary>A chest in the colour of a tier's rarity.</summary>
        public UiChest(ChestTier tier) : this(UIFactory.ChestColor(tier)) { }

        public Color Tint
        {
            get => tint;
            set { tint = value; MarkDirtyRepaint(); }
        }

        private float s;
        private Vector2 o;
        private Painter2D p;

        private Vector2 V(float x, float y) => new Vector2(o.x + x * s, o.y + y * s);

        private void Draw(MeshGenerationContext ctx)
        {
            float w = resolvedStyle.width, h = resolvedStyle.height;
            if (float.IsNaN(w) || float.IsNaN(h) || w <= 0f || h <= 0f) return;

            // The chest occupies x 23..177, y 36..180 of the grid. A whole chest is centred in its square;
            // a piece is fitted so the chest spans the element, and both pieces use the same fit.
            if (part == Part.Whole)
            {
                s = Mathf.Min(w, h) / 200f;
                o = new Vector2((w - 200f * s) * 0.5f, (h - 200f * s) * 0.5f);
            }
            else
            {
                s = Mathf.Min(w / 154f, h / 144f);
                o = new Vector2(w * 0.5f - 100f * s, h * 0.5f - 108f * s);
            }

            p = ctx.painter2D;
            p.lineJoin = LineJoin.Round;
            p.lineCap = LineCap.Round;

            bool lidOn = part != Part.Body, bodyOn = part != Part.Lid;

            if (lidOn) DrawLid();
            if (bodyOn) DrawBody();
            if (lidOn) DrawRim();
            DrawRivets(lidOn, bodyOn);
            if (lidOn) DrawLock();
        }

        private void DrawLid()
        {
            Color lid = new Color(0.086f, 0.125f, 0.227f);      // #16203A
            Color band = tint.WithAlpha(0.55f);

            // Lid: a dome.
            p.BeginPath();
            p.MoveTo(V(28f, 98f));
            p.LineTo(V(28f, 78f));
            p.QuadraticCurveTo(V(28f, 36f), V(100f, 36f));
            p.QuadraticCurveTo(V(172f, 36f), V(172f, 78f));
            p.LineTo(V(172f, 98f));
            p.ClosePath();
            Filled(lid, tint, 4f);

            Poly(band, V(54f, 98f), V(54f, 51f), V(70f, 44f), V(70f, 98f));
            Poly(band, V(130f, 44f), V(146f, 51f), V(146f, 98f), V(130f, 98f));

            // A highlight along the dome.
            p.strokeColor = new Color(1f, 1f, 1f, 0.25f);
            p.lineWidth = 4f * s;
            p.BeginPath();
            p.MoveTo(V(40f, 76f));
            p.QuadraticCurveTo(V(46f, 52f), V(96f, 47f));
            p.Stroke();
        }

        private void DrawBody()
        {
            Color body = new Color(0.051f, 0.078f, 0.149f);     // #0D1426
            Color band = tint.WithAlpha(0.55f);
            Color bandSoft = tint.WithAlpha(0.85f);

            // Body: a shallow trapezoid.
            p.BeginPath();
            p.MoveTo(V(32f, 100f));
            p.LineTo(V(168f, 100f));
            p.LineTo(V(162f, 180f));
            p.LineTo(V(38f, 180f));
            p.ClosePath();
            Filled(body, tint, 4f);

            Poly(band, V(54f, 100f), V(70f, 100f), V(71f, 178f), V(57f, 178f));
            Poly(band, V(130f, 100f), V(146f, 100f), V(143f, 178f), V(129f, 178f));
            Poly(bandSoft, V(37f, 166f), V(163f, 166f), V(162f, 180f), V(38f, 180f));
        }

        /// <summary>The rim band across the join. It belongs to the lid, so it leaves with it.</summary>
        private void DrawRim()
        {
            Poly(tint, V(23f, 91f), V(177f, 91f), V(177f, 104f), V(23f, 104f));
            Poly(ArcadeTheme.BgDeep.WithAlpha(0.55f), V(26f, 104f), V(174f, 104f), V(174f, 108f), V(26f, 108f));
        }

        private void DrawRivets(bool onLid, bool onBody)
        {
            Color rivet = ArcadeTheme.Ink;
            if (onLid)
            {
                foreach (var pt in new[] { new Vector2(62f, 60f), new Vector2(62f, 82f),
                                           new Vector2(138f, 60f), new Vector2(138f, 82f) })
                {
                    Disc(pt.x, pt.y, 2.6f, rivet);
                }
            }

            if (onBody)
            {
                foreach (var pt in new[] { new Vector2(62f, 120f), new Vector2(63f, 156f),
                                           new Vector2(138f, 120f), new Vector2(137f, 156f) })
                {
                    Disc(pt.x, pt.y, 2.6f, rivet);
                }
            }
        }

        /// <summary>The shield lock hangs from the lid over the top of the body, so it lifts with it.</summary>
        private void DrawLock()
        {
            // Shield lock, with its keyhole.
            p.BeginPath();
            p.MoveTo(V(84f, 84f));
            p.LineTo(V(116f, 84f));
            p.LineTo(V(116f, 114f));
            p.LineTo(V(100f, 128f));
            p.LineTo(V(84f, 114f));
            p.ClosePath();
            Filled(ArcadeTheme.BgDeep, tint, 3.5f);

            Disc(100f, 101f, 5f, tint);
            Poly(tint, V(97.5f, 103f), V(102.5f, 103f), V(104.5f, 114f), V(95.5f, 114f));
        }

        private void Filled(Color fill, Color stroke, float width)
        {
            p.fillColor = fill;
            p.Fill();
            p.strokeColor = stroke;
            p.lineWidth = width * s;
            p.Stroke();
        }

        private void Poly(Color fill, params Vector2[] points)
        {
            p.BeginPath();
            p.MoveTo(points[0]);
            for (int i = 1; i < points.Length; i++) p.LineTo(points[i]);
            p.ClosePath();
            p.fillColor = fill;
            p.Fill();
        }

        private void Disc(float cx, float cy, float r, Color color)
        {
            p.BeginPath();
            p.Arc(V(cx, cy), r * s, Angle.Degrees(0f), Angle.Degrees(360f));
            p.ClosePath();
            p.fillColor = color;
            p.Fill();
        }
    }

    /// <summary>Pictures of things the player owns, for the UI Toolkit screens.</summary>
    public static class UiArt
    {
        /// <summary>
        /// A cosmetic's picture: its rendered thumbnail on a tile tinted by its rarity, or — for the
        /// ones with no render yet — a glyph for the kind of thing it is. League badges are drawn as a
        /// hexagon in the league's metal with the ranked cup in it.
        ///
        /// Fills the element it returns; give that a size from the stylesheet. The tint stays behind a
        /// thumbnail (they are transparent), because the colour is how a wall of cards is scanned.
        /// </summary>
        public static VisualElement Cosmetic(CosmeticItem item)
        {
            Color tint = UIFactory.RarityColor(item.Rarity);
            var art = new VisualElement { pickingMode = PickingMode.Ignore };
            art.AddToClassList("cosart");
            art.style.backgroundColor = Color.Lerp(ArcadeTheme.BgDeep, tint, 0.14f);

            if (item.Kind == CosmeticKind.Badge)
            {
                art.Add(Badge(item, 84f));
                return art;
            }

            // A name style is shown as a name in that style. The default is the plain name.
            if (item.Kind == CosmeticKind.NameStyle)
            {
                var sample = new UiShineLabel();
                sample.AddToClassList("cosart__name");
                sample.AddToClassList("f-display");
                bool gold = item.Id == LevelPath.GoldNameId;
                sample.Set("PLAYER", isMine: false, alwaysGold: gold);
                art.Add(sample);
                return art;
            }

            Sprite thumb = CosmeticThumbnails.For(item.Id);
            if (thumb != null)
            {
                var img = new VisualElement { pickingMode = PickingMode.Ignore };
                img.AddToClassList("cosart__img");
                img.style.backgroundImage = new StyleBackground(thumb);
                art.Add(img);
            }
            else
            {
                art.Add(new UiIcon(KindGlyph(item.Kind), tint, 2f));
            }

            return art;
        }

        /// <summary>A badge as a hexagon: the King badge is a gold crown, a league badge is that league's
        /// cup in its metal.</summary>
        public static VisualElement Badge(CosmeticItem item, float size) =>
            item.Id == LevelPath.KingBadgeId ? KingBadge(size) : LeagueBadge(LeagueBadges.LeagueOf(item.Id), size);

        /// <summary>The badge worn beside the player's name, or null when none is worn.</summary>
        public static VisualElement WornBadge(float size)
        {
            CosmeticItem worn = Inventory.Equipped(CosmeticKind.Badge);
            return worn.Valid ? Badge(worn, size) : null;
        }

        /// <summary>The crown in a hexagon, in gold — the prize for reaching level 60.</summary>
        public static VisualElement KingBadge(float size)
        {
            var hex = new UiHex { Edge = ArcadeTheme.Reward, Fill = ArcadeTheme.BgDeep };
            hex.style.width = size;
            hex.style.height = size;
            hex.style.flexShrink = 0f;
            hex.style.alignItems = Align.Center;
            hex.style.justifyContent = Justify.Center;
            var crown = new UiIcon(UiIcon.Glyph.Crown, ArcadeTheme.Reward, 2f);
            crown.style.width = size * 0.56f;
            crown.style.height = size * 0.56f;
            hex.Add(crown);
            return hex;
        }

        /// <summary>The ranked cup in a hexagon in a league's metal.</summary>
        public static VisualElement LeagueBadge(League league, float size)
        {
            Color metal = UIFactory.LeagueColor(league);
            var hex = new UiHex { Edge = metal, Fill = ArcadeTheme.BgDeep };
            hex.style.width = size;
            hex.style.height = size;
            hex.style.alignItems = Align.Center;
            hex.style.justifyContent = Justify.Center;
            var cup = new UiIcon(UiIcon.Glyph.Trophy, metal, 2f);
            cup.style.width = size * 0.5f;
            cup.style.height = size * 0.5f;
            hex.Add(cup);
            return hex;
        }

        /// <summary>The glyph that says which part of the table a cosmetic dresses.</summary>
        public static UiIcon.Glyph KindGlyph(CosmeticKind kind)
        {
            switch (kind)
            {
                case CosmeticKind.FieldSkin: return UiIcon.Glyph.Table;
                case CosmeticKind.BallSkin: return UiIcon.Glyph.Globe;
                case CosmeticKind.FigureSkin: return UiIcon.Glyph.Person;
                case CosmeticKind.Background: return UiIcon.Glyph.Star;
                case CosmeticKind.NameStyle: return UiIcon.Glyph.Pencil;
                default: return UiIcon.Glyph.Chest;
            }
        }
    }
}
