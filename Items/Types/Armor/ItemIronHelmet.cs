using VoxelEngine.Rendering;
using VoxelEngine.Utils;

namespace VoxelEngine.Items;

public class ItemIronHelmet : ItemArmor
{
    public override ItemType Type => ItemType.IronHelmet;
    public override string Name => "Iron Helmet";
    public override TextureCoords ItemCoords => UvHelper.FromTileCoords(2, 11);
    public override ArmorSlot? ArmorSlot => Items.ArmorSlot.Head;
    public override ArmorTier? ArmorTier => Items.ArmorTier.Iron;
    public override int MaxDurability => 166;
    public override int ArmorPoints => 2;
}
