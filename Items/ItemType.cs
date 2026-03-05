// Big enum that holds reference to the items in the game | DA | 3/5/26
namespace VoxelEngine.Items;

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

public enum ToolTier : byte
{
    None,
    Wood,
    Stone,
    Iron,
    Gold,
    Diamond
}

public enum ArmorSlot : byte
{
    Head,
    Chest,
    Legs,
    Feet
}

public enum ArmorTier : byte
{
    Leather,
    Iron,
    Gold,
    Diamond
}