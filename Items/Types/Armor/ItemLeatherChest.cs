using VoxelEngine.Rendering;
using VoxelEngine.Utils;

namespace VoxelEngine.Items;

/// <summary>Leather-tier chestplate (Chest slot); lowest tier, grants 3 armor points.</summary>
public class ItemLeatherChest : ItemArmor
{
    public override ItemType Type => ItemType.LeatherChest;
    public override string Name => "Leather Chestplate";
    public override TextureCoords ItemCoords => UvHelper.FromTileCoords(0, 10);
    public override ArmorSlot? ArmorSlot => Items.ArmorSlot.Chest;
    public override ArmorTier? ArmorTier => Items.ArmorTier.Leather;
    public override int MaxDurability => 81;
    public override int ArmorPoints => 3;
}
