using VoxelEngine.Rendering;
using VoxelEngine.Utils;

namespace VoxelEngine.Items;

/// <summary>Gold-tier leggings (Legs slot). Fast-degrading like all gold gear (low durability), grants 3 armor points.</summary>
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
