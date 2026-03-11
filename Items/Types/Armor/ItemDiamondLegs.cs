using VoxelEngine.Rendering;
using VoxelEngine.Utils;

namespace VoxelEngine.Items;

public class ItemDiamondLegs : ItemArmor
{
    public override ItemType Type => ItemType.DiamondLegs;
    public override string Name => "Diamond Leggings";
    public override TextureCoords ItemCoords => UvHelper.FromTileCoords(1, 9);
    public override ArmorSlot? ArmorSlot => Items.ArmorSlot.Legs;
    public override ArmorTier? ArmorTier => Items.ArmorTier.Diamond;
    public override int MaxDurability => 496;
    public override int ArmorPoints => 6;
}
