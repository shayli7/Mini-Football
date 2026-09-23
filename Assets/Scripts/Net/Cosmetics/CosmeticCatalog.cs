using System.Collections.Generic;

namespace TableFootball.Net
{
    /// <summary>
    /// Every cosmetic the game knows about, in one list.
    ///
    /// PLACEHOLDER CONTENT. The names and prices are real and the plumbing around them is real — the
    /// store sells these, chests roll these, the inventory persists these — but none of them has art
    /// yet, so each draws as a rarity-tinted swatch. Adding a real skin is one row here; nothing else
    /// in the game holds a list of skins.
    ///
    /// Kept in <c>Net/</c> beside the rest of the account data rather than in <c>UI/</c>, because the
    /// catalogue is what the player OWNS, not how it looks. The UI decides colours; this decides what
    /// exists.
    /// </summary>
    public static class CosmeticCatalog
    {
        /// <summary>
        /// The whole catalogue. Order matters only for the store grid, which shows items in the order
        /// they appear here within each tab — cheapest first reads as a ladder to climb.
        /// </summary>
        public static readonly CosmeticItem[] All =
        {
            // -- Field skins --------------------------------------------------------------------
            // Real art, painted against the Field box's own unwrap — see Gameplay/FieldSkins. The
            // default carries no texture at all: it is the model's own turf, left alone.
            new CosmeticItem("field.classic",       "Classic Turf",  CosmeticKind.FieldSkin,  Rarity.Common, 0, isDefault: true),
            new CosmeticItem("field.deep_blue",     "Deep Blue",     CosmeticKind.FieldSkin,  Rarity.Common,    300),
            new CosmeticItem("field.court",         "Indoor Court",  CosmeticKind.FieldSkin,  Rarity.Common,    300),
            new CosmeticItem("field.wild_west",     "Wild West",     CosmeticKind.FieldSkin,  Rarity.Rare,      750),
            new CosmeticItem("field.pitch_perfect", "Pitch Perfect", CosmeticKind.FieldSkin,  Rarity.Rare,      750),
            new CosmeticItem("field.neon_rave",     "Neon Rave",     CosmeticKind.FieldSkin,  Rarity.Rare,      750),
            new CosmeticItem("field.chessboard",    "Chessboard",    CosmeticKind.FieldSkin,  Rarity.Epic,     1600),

            // -- Ball skins ---------------------------------------------------------------------
            // The first kind with REAL art. Each of these is an equirectangular texture set under
            // Resources/BallSkins, authored in Blender against the ball's own UV layout — see
            // Gameplay/BallSkins, which turns an id here into the material the ball wears. The
            // default deliberately has no texture at all: it is the model's own material, left alone.
            new CosmeticItem("ball.classic",      "Classic Ball",       CosmeticKind.BallSkin,   Rarity.Common,    0, isDefault: true),
            new CosmeticItem("ball.retro_orange", "Retro Orange",       CosmeticKind.BallSkin,   Rarity.Common,    300),
            new CosmeticItem("ball.solid_red",    "Solid Red",          CosmeticKind.BallSkin,   Rarity.Common,    300),
            new CosmeticItem("ball.solid_blue",   "Solid Blue",         CosmeticKind.BallSkin,   Rarity.Common,    300),
            new CosmeticItem("ball.soccer",       "Soccer Ball",        CosmeticKind.BallSkin,   Rarity.Rare,      750),
            new CosmeticItem("ball.basketball",   "Basketball",         CosmeticKind.BallSkin,   Rarity.Rare,      750),
            new CosmeticItem("ball.camo",         "Camouflage",         CosmeticKind.BallSkin,   Rarity.Rare,      750),
            new CosmeticItem("ball.disco",        "Disco Ball",         CosmeticKind.BallSkin,   Rarity.Epic,      1600),
            new CosmeticItem("ball.ice",          "Ice Cube",           CosmeticKind.BallSkin,   Rarity.Epic,      1600),
            new CosmeticItem("ball.golden",       "Golden Trophy Ball", CosmeticKind.BallSkin,   Rarity.Legendary, 3200),

            // -- Figure skins -------------------------------------------------------------------
            // Every one keeps the kit slot team-coloured, so red and blue are always tellable apart
            // — see Gameplay/FigureSkins. Trojan Warriors is the only skin whose goalkeeper differs.
            new CosmeticItem("figure.classic",    "Classic Kit",     CosmeticKind.FigureSkin, Rarity.Common, 0, isDefault: true),
            new CosmeticItem("figure.yellow",     "Yellow",          CosmeticKind.FigureSkin, Rarity.Common,    300),
            new CosmeticItem("figure.black",      "Black",           CosmeticKind.FigureSkin, Rarity.Common,    300),
            new CosmeticItem("figure.green",      "Green",           CosmeticKind.FigureSkin, Rarity.Common,    300),
            new CosmeticItem("figure.stripes",    "Stripes",         CosmeticKind.FigureSkin, Rarity.Rare,      750),
            new CosmeticItem("figure.gladiators", "Gladiators",      CosmeticKind.FigureSkin, Rarity.Rare,      750),
            new CosmeticItem("figure.robots",     "Robots",          CosmeticKind.FigureSkin, Rarity.Epic,     1600),
            new CosmeticItem("figure.trojan",     "Trojan Warriors", CosmeticKind.FigureSkin, Rarity.Legendary, 3200),

            // -- Backgrounds --------------------------------------------------------------------
            // The room the table stands in: a floor under the table during a match (see
            // BackgroundSkinner) plus a skybox for the front end. The default is neither — the menu
            // keeps the flat backdrop it has always had, so equipping nothing changes nothing.
            new CosmeticItem("background.classic",     "Plain Backdrop", CosmeticKind.Background, Rarity.Common, 0, isDefault: true),
            new CosmeticItem("background.living_room", "Living Room",    CosmeticKind.Background, Rarity.Common,    300),
            new CosmeticItem("background.dark_room",   "Dark Room",      CosmeticKind.Background, Rarity.Common,    300),
            new CosmeticItem("background.disco_room",  "Disco Room",     CosmeticKind.Background, Rarity.Rare,      750),
            new CosmeticItem("background.royal_palace","Royal Palace",   CosmeticKind.Background, Rarity.Epic,     1600),

            // -- League badges (earned, never sold) ----------------------------------------------
            // One per rung of the ladder. Priced 0 and of kind Badge, which keeps them out of the
            // store grid and out of every chest pool without a second rule anywhere. See LeagueBadges.
            new CosmeticItem("badge.bronze",   "Bronze Badge",   CosmeticKind.Badge,      Rarity.Common,    0),
            new CosmeticItem("badge.silver",   "Silver Badge",   CosmeticKind.Badge,      Rarity.Rare,      0),
            new CosmeticItem("badge.gold",     "Gold Badge",     CosmeticKind.Badge,      Rarity.Epic,      0),
            new CosmeticItem("badge.diamond",  "Diamond Badge",  CosmeticKind.Badge,      Rarity.Legendary, 0)
        };

        private static Dictionary<string, CosmeticItem> byId;

        /// <summary>
        /// The item with this id, or an invalid <see cref="CosmeticItem"/> (<c>Valid == false</c>) if
        /// there is none. Missing rather than throwing on purpose: a saved inventory can name an item
        /// a later build removed, and that must degrade to "you no longer have it" rather than taking
        /// down the screen drawing it.
        /// </summary>
        public static CosmeticItem Get(string id)
        {
            if (string.IsNullOrEmpty(id))
            {
                return default;
            }

            if (byId == null)
            {
                byId = new Dictionary<string, CosmeticItem>(All.Length);
                foreach (var item in All)
                {
                    byId[item.Id] = item;
                }
            }

            return byId.TryGetValue(id, out CosmeticItem found) ? found : default;
        }

        /// <summary>Every item of one kind, in catalogue order.</summary>
        public static List<CosmeticItem> OfKind(CosmeticKind kind)
        {
            var list = new List<CosmeticItem>();
            foreach (var item in All)
            {
                if (item.Kind == kind) list.Add(item);
            }
            return list;
        }

        /// <summary>The item worn when nothing is equipped for a kind. Every kind has exactly one.</summary>
        public static CosmeticItem DefaultFor(CosmeticKind kind)
        {
            foreach (var item in All)
            {
                if (item.Kind == kind && item.IsDefault) return item;
            }
            return default;
        }

        /// <summary>
        /// The store's tabs, in order: the four kinds it sells, then badges.
        ///
        /// Badges are on the end even though nothing there is purchasable, and that is deliberate. The
        /// store is where a player goes to change how they look, and a badge is the one cosmetic that
        /// cannot be bought — putting it on its own screen would hide the wardrobe behind two doors
        /// and leave the store claiming to be the whole collection when it was not. The tab labels
        /// itself as earned rather than sold, so it never pretends otherwise.
        /// </summary>
        public static readonly CosmeticKind[] TabKinds =
        {
            CosmeticKind.FieldSkin, CosmeticKind.BallSkin, CosmeticKind.FigureSkin,
            CosmeticKind.Background, CosmeticKind.Badge
        };

        /// <summary>The tab label for a kind. Plural, because a tab holds several.</summary>
        public static string KindName(CosmeticKind kind)
        {
            switch (kind)
            {
                case CosmeticKind.FieldSkin: return "Fields";
                case CosmeticKind.BallSkin: return "Balls";
                case CosmeticKind.FigureSkin: return "Players";
                case CosmeticKind.TableSkin: return "Tables";
                case CosmeticKind.Background: return "Rooms";
                case CosmeticKind.Badge: return "Badges";
                default: return "Items";
            }
        }

        /// <summary>The singular noun, for a reward line ("FIELD SKIN") rather than a tab.</summary>
        public static string KindLabel(CosmeticKind kind)
        {
            switch (kind)
            {
                case CosmeticKind.FieldSkin: return "Field Skin";
                case CosmeticKind.BallSkin: return "Ball Skin";
                case CosmeticKind.FigureSkin: return "Player Skin";
                case CosmeticKind.TableSkin: return "Table Skin";
                case CosmeticKind.Background: return "Background";
                case CosmeticKind.Badge: return "Badge";
                default: return "Item";
            }
        }

        public static string RarityName(Rarity rarity)
        {
            switch (rarity)
            {
                case Rarity.Common: return "Common";
                case Rarity.Rare: return "Rare";
                case Rarity.Epic: return "Epic";
                case Rarity.Legendary: return "Legendary";
                default: return "Common";
            }
        }
    }
}
