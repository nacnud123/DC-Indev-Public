using VoxelEngine.Rendering;
using VoxelEngine.Utils;

namespace VoxelEngine.Items;

/// <summary>Diamond-tier chestplate (Chest slot); highest durability tier, grants 8 armor points (best in the game).</summary>
public class ItemDiamondChest : ItemArmor
{
    public override ItemType Type => ItemType.DiamondChest;
    public override string Name => "Diamond Chestplate";
    public override TextureCoords ItemCoords => UvHelper.FromTileCoords(1, 10);
    public override ArmorSlot? ArmorSlot => Items.ArmorSlot.Chest;
    public override ArmorTier? ArmorTier => Items.ArmorTier.Diamond;
    public override int MaxDurability => 529;
    public override int ArmorPoints => 8;
}
