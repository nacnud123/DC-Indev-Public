using VoxelEngine.Rendering;
using VoxelEngine.Utils;

namespace VoxelEngine.Items;

public class ItemGoldChest : ItemArmor
{
    public override ItemType Type => ItemType.GoldChest;
    public override string Name => "Gold Chestplate";
    public override TextureCoords ItemCoords => UvHelper.FromTileCoords(3, 10);
    public override ArmorSlot? ArmorSlot => Items.ArmorSlot.Chest;
    public override ArmorTier? ArmorTier => Items.ArmorTier.Gold;
    public override int MaxDurability => 113;
    public override int ArmorPoints => 5;
}
