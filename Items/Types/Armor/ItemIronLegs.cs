using VoxelEngine.Rendering;
using VoxelEngine.Utils;

namespace VoxelEngine.Items;

public class ItemIronLegs : ItemArmor
{
    public override ItemType Type => ItemType.IronLegs;
    public override string Name => "Iron Leggings";
    public override TextureCoords ItemCoords => UvHelper.FromTileCoords(2, 9);
    public override ArmorSlot? ArmorSlot => Items.ArmorSlot.Legs;
    public override ArmorTier? ArmorTier => Items.ArmorTier.Iron;
    public override int MaxDurability => 226;
    public override int ArmorPoints => 5;
}
