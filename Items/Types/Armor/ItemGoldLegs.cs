using VoxelEngine.Rendering;
using VoxelEngine.Utils;

namespace VoxelEngine.Items;

public class ItemGoldLegs : ItemArmor
{
    public override ItemType Type => ItemType.GoldLegs;
    public override string Name => "Gold Leggings";
    public override TextureCoords ItemCoords => UvHelper.FromTileCoords(3, 9);
    public override ArmorSlot? ArmorSlot => Items.ArmorSlot.Legs;
    public override ArmorTier? ArmorTier => Items.ArmorTier.Gold;
    public override int MaxDurability => 106;
    public override int ArmorPoints => 3;
}
