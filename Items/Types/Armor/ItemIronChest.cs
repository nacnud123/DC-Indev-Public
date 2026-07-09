using VoxelEngine.Rendering;
using VoxelEngine.Utils;

namespace VoxelEngine.Items;

/// <summary>Iron-tier chestplate (Chest slot); grants 6 armor points.</summary>
public class ItemIronChest : ItemArmor
{
    public override ItemType Type => ItemType.IronChest;
    public override string Name => "Iron Chestplate";
    public override TextureCoords ItemCoords => UvHelper.FromTileCoords(2, 10);
    public override ArmorSlot? ArmorSlot => Items.ArmorSlot.Chest;
    public override ArmorTier? ArmorTier => Items.ArmorTier.Iron;
    public override int MaxDurability => 241;
    public override int ArmorPoints => 6;
}
