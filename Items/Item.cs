// Abstract base class for all items, like Block.cs but for items | DA | 3/8/26

using VoxelEngine.Rendering;
using VoxelEngine.Terrain;

namespace VoxelEngine.Items;

/// <summary>
/// Abstract base for every non-block item definition in the game (tools, armor, food, resources, etc.). One concrete subclass exists per <see cref="ItemType"/> and a single instance of each is held by <see cref="ItemRegistry"/> (mirrors the Block/BlockRegistry pattern). Subclasses override the virtual properties below to opt into tool, armor, or food behavior instead of implementing separate interfaces.
/// </summary>
public abstract class Item
{
    /// <summary>The enum value this instance represents; used as the registry lookup key.</summary>
    public abstract ItemType Type { get; }

    /// <summary>Display name shown in the UI (inventory tooltips, etc.).</summary>
    public abstract string Name { get; }

    /// <summary>Atlas UV rectangle for this item's icon in Resources/Items.png.</summary>
    public abstract TextureCoords ItemCoords { get; }

    /// <summary>Which tool category this item belongs to; None means "not a tool".</summary>
    public virtual ToolType ToolType => ToolType.None;

    /// <summary>Material tier (Wood/Stone/Iron/Gold/Diamond) used for mining-speed/durability lookups.</summary>
    public virtual ToolTier ToolTier => ToolTier.None;

    /// <summary>Max uses before the item breaks; -1 means the item has no durability (unbreakable/non-tool).</summary>
    public virtual int MaxDurability => -1;

    /// <summary>Multiplier applied to block-breaking speed when this item is held.</summary>
    public virtual float MiningSpeed => 1f;

    /// <summary>Melee damage dealt to entities when attacking with this item.</summary>
    public virtual int AttackDamage => 1;

    /// <summary>True if this item can be eaten via the food system.</summary>
    public virtual bool IsFood => false;

    /// <summary>Hunger/saturation restored when this food item is consumed.</summary>
    public virtual int FoodRestore => 0;

    /// <summary>Max quantity of this item allowed in a single inventory slot (tools/armor are always 1).</summary>
    public virtual int MaxStackSize => 1;

    /// <summary>When true, using this item ignores the normal block-under-cursor raycast (e.g. thrown/placed-elsewhere items).</summary>
    public virtual bool SkipBlockRaycast => false;

    /// <summary>Equipment slot this item occupies if it is armor; null for non-armor items.</summary>
    public virtual ArmorSlot? ArmorSlot => null;

    /// <summary>Material tier if this item is armor; null for non-armor items.</summary>
    public virtual ArmorTier? ArmorTier => null;

    /// <summary>Defense points contributed while this armor piece is equipped.</summary>
    public virtual int ArmorPoints => 0;

    /// <summary>Convenience flag: true when this item has a real (non-None) ToolType.</summary>
    public bool IsTool => ToolType != ToolType.None;

    /// <summary>Convenience flag: true when this item occupies an armor slot.</summary>
    public bool IsArmor => ArmorSlot.HasValue;

    /// <summary>
    /// Right-click/use-item hook. blockPos is the block being targeted, placePos is the adjacent empty position (if any) where a new block/entity would be placed. Returns true if the item handled the interaction (consuming the click); default is a no-op.
    /// </summary>
    public virtual bool OnUse(World world, Vector3i blockPos, Vector3i? placePos) => false;
}
