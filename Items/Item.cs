// Abstract base class for all items, like Block.cs but for items | DA | 3/8/26
using OpenTK.Mathematics;
using VoxelEngine.Rendering;
using VoxelEngine.Terrain;

namespace VoxelEngine.Items;

public abstract class Item
{
    public abstract ItemType Type { get; }
    public abstract string Name { get; }
    public abstract TextureCoords ItemCoords { get; }

    public virtual ToolType ToolType => ToolType.None;
    public virtual ToolTier ToolTier => ToolTier.None;
    public virtual int MaxDurability => -1;
    public virtual float MiningSpeed => 1f;
    public virtual int AttackDamage => 1;
    public virtual bool IsFood => false;
    public virtual int FoodRestore => 0;
    public virtual int MaxStackSize => 1;
    public virtual bool SkipBlockRaycast => false;
    public virtual ArmorSlot? ArmorSlot => null;
    public virtual ArmorTier? ArmorTier => null;
    public virtual int ArmorPoints => 0;

    public bool IsTool => ToolType != ToolType.None;
    public bool IsArmor => ArmorSlot.HasValue;

    public virtual bool OnUse(World world, Vector3i blockPos, Vector3i? placePos) => false;
}
