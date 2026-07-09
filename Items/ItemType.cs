// Big enum that holds reference to the items in the game | DA | 3/5/26
namespace VoxelEngine.Items;

/// <summary>
/// Identifies every non-block item in the game. Each value (other than None) must have a matching concrete <see cref="Item"/> subclass registered in <see cref="ItemRegistry"/>. Grouped by category via comments below purely for readability — the numeric byte values are assigned sequentially and are not meaningful on their own.
/// </summary>
public enum ItemType : byte
{
    None = 0,
    
    // Tools
    WoodPickaxe,
    WoodSword,
    WoodAxe,
    WoodShovel,
    WoodHoe,
    
    StonePickaxe,
    StoneSword,
    StoneAxe,
    StoneShovel,
    StoneHoe,
    
    IronPickaxe,
    IronSword,
    IronAxe,
    IronShovel,
    IronHoe,
    
    GoldPickaxe,
    GoldSword,
    GoldAxe,
    GoldShovel,
    GoldHoe,
    
    DiamondPickaxe,
    DiamondSword,
    DiamondAxe,
    DiamondShovel,
    DiamondHoe,
    
    FlintSteel,
    
    // Crafting
    Stick,

    // Resources
    Diamond,
    Coal,
    IronBar,
    GoldBar,
    Sulfur,
    Flint,
    Bone,
    Feather,
    String,

    // Decorative / misc
    Painting,

    // Food & farming
    Bread,
    Wheat,
    EmptyBowl,
    Stew,
    Seeds,
    RawPork,
    CookedPork,
    Apple,

    // Ranged
    Bow,
    Arrow,

    // Armor
    LeatherHelmet,
    LeatherChest,
    LeatherLegs,
    LeatherBoots,

    IronHelmet,
    IronChest,
    IronLegs,
    IronBoots,

    GoldHelmet,
    GoldChest,
    GoldLegs,
    GoldBoots,

    DiamondHelmet,
    DiamondChest,
    DiamondLegs,
    DiamondBoots,
}

/// <summary>Categorizes a tool item by its function; used to look up mining-speed rules and animations.</summary>
public enum ToolType : byte
{
    None,
    Pickaxe,
    Sword,
    Axe,
    Shovel,
    Hoe,
    Bow,
    Misc
}

/// <summary>Material tier for tools; drives mining speed, durability, and attack damage scaling.</summary>
public enum ToolTier : byte
{
    None,
    Wood,
    Stone,
    Iron,
    Gold,
    Diamond
}

/// <summary>Equipment slot an armor piece occupies (helmet/chestplate/leggings/boots).</summary>
public enum ArmorSlot : byte
{
    Head,
    Chest,
    Legs,
    Feet
}

/// <summary>Material tier for armor; drives armor points per piece.</summary>
public enum ArmorTier : byte
{
    Leather,
    Iron,
    Gold,
    Diamond
}