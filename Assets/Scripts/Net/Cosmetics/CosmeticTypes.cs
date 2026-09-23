namespace TableFootball.Net
{
    /// <summary>
    /// What part of the game a cosmetic dresses. One kind can have exactly one item equipped at a
    /// time, which is what makes this an enum rather than a free-form tag: the store's tabs, the
    /// inventory's "currently equipped" slots and the chest's loot pools are all the same list.
    ///
    /// <see cref="Badge"/> is the odd one out and deliberately so — badges are EARNED from the ranked
    /// ladder rather than bought, so they never appear in the store or in a chest. See
    /// <see cref="LeagueBadges"/>.
    /// </summary>
    public enum CosmeticKind
    {
        FieldSkin = 0,
        BallSkin = 1,
        FigureSkin = 2,
        TableSkin = 3,
        Badge = 4,

        /// <summary>The room the table stands in — a skybox, since the scene has no environment
        /// geometry of its own. See <c>Gameplay/BackgroundSkins</c>.</summary>
        Background = 5
    }

    /// <summary>
    /// How rare an item is. Order is load-bearing: a higher value is rarer, so chest odds and price
    /// bands can both be derived from it rather than restated per item.
    /// </summary>
    public enum Rarity
    {
        Common = 0,
        Rare = 1,
        Epic = 2,
        Legendary = 3
    }

    /// <summary>
    /// One cosmetic in the catalogue — everything the store, the chest and the inventory need to know
    /// about it. A value type with no art reference: the visuals are PLACEHOLDERS drawn from theme
    /// primitives today (see <c>UIFactory.CosmeticSwatch</c>), so an item is currently a name, a
    /// rarity and a price and nothing else.
    ///
    /// When real art arrives it is one field on this struct plus one line in the swatch, and no screen
    /// changes — which is the reason the catalogue is data rather than a screen full of hand-built
    /// cards.
    /// </summary>
    public readonly struct CosmeticItem
    {
        /// <summary>Stable identifier, persisted in the inventory. Never renamed once shipped —
        /// a changed id is a lost item on every device that already owned it.</summary>
        public readonly string Id;

        public readonly string Name;
        public readonly CosmeticKind Kind;
        public readonly Rarity Rarity;

        /// <summary>Coin price. Zero means NOT FOR SALE — the defaults everyone starts with, and the
        /// league badges, which are earned. The store shows only items priced above zero.</summary>
        public readonly int Price;

        /// <summary>Owned from the first launch, and the fallback when nothing is equipped. Exactly
        /// one per kind, which <see cref="CosmeticCatalog"/> asserts on first use.</summary>
        public readonly bool IsDefault;

        /// <summary>False for items a chest must never roll — the defaults, and the earned badges.</summary>
        public bool CanDropFromChest => !IsDefault && Kind != CosmeticKind.Badge;

        /// <summary>True when this item belongs in the store's grid.</summary>
        public bool IsForSale => Price > 0 && Kind != CosmeticKind.Badge;

        /// <summary>An unset item, returned by lookups that miss. <c>Id == null</c> is the test.</summary>
        public bool Valid => !string.IsNullOrEmpty(Id);

        public CosmeticItem(string id, string name, CosmeticKind kind, Rarity rarity, int price,
                            bool isDefault = false)
        {
            Id = id;
            Name = name;
            Kind = kind;
            Rarity = rarity;
            Price = price;
            IsDefault = isDefault;
        }
    }
}
