using VoxelEngine.Rendering;
using VoxelEngine.Utils;

namespace VoxelEngine.Items;

public class ItemLeatherLegs : ItemArmor
{
    public override ItemType Type => ItemType.LeatherLegs;
    public override string Name => "Leather Leggings";
    public override TextureCoords ItemCoords => UvHelper.FromTileCoords(0, 9);
    public override ArmorSlot? ArmorSlot => Items.ArmorSlot.Legs;
    public override ArmorTier? ArmorTier => Items.ArmorTier.Leather;
    public override int MaxDurability => 76;
    public override int ArmorPoints => 2;
}
