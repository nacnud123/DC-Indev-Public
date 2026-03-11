using VoxelEngine.Rendering;
using VoxelEngine.Utils;

namespace VoxelEngine.Items;

public class ItemDiamondHelmet : ItemArmor
{
    public override ItemType Type => ItemType.DiamondHelmet;
    public override string Name => "Diamond Helmet";
    public override TextureCoords ItemCoords => UvHelper.FromTileCoords(1, 11);
    public override ArmorSlot? ArmorSlot => Items.ArmorSlot.Head;
    public override ArmorTier? ArmorTier => Items.ArmorTier.Diamond;
    public override int MaxDurability => 364;
    public override int ArmorPoints => 3;
}
