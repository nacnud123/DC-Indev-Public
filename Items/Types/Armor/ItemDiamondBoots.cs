using VoxelEngine.Rendering;
using VoxelEngine.Utils;

namespace VoxelEngine.Items;

public class ItemDiamondBoots : ItemArmor
{
    public override ItemType Type => ItemType.DiamondBoots;
    public override string Name => "Diamond Boots";
    public override TextureCoords ItemCoords => UvHelper.FromTileCoords(1, 8);
    public override ArmorSlot? ArmorSlot => Items.ArmorSlot.Feet;
    public override ArmorTier? ArmorTier => Items.ArmorTier.Diamond;
    public override int MaxDurability => 430;
    public override int ArmorPoints => 3;
}
