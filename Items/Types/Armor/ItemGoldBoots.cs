using VoxelEngine.Rendering;
using VoxelEngine.Utils;

namespace VoxelEngine.Items;

/// <summary>Gold-tier boots (Feet slot). Fast-degrading like all gold gear (low durability), grants 1 armor point.</summary>
public class ItemGoldBoots : ItemArmor
{
    public override ItemType Type => ItemType.GoldBoots;
    public override string Name => "Gold Boots";
    public override TextureCoords ItemCoords => UvHelper.FromTileCoords(3, 8);
    public override ArmorSlot? ArmorSlot => Items.ArmorSlot.Feet;
    public override ArmorTier? ArmorTier => Items.ArmorTier.Gold;
    public override int MaxDurability => 92;
    public override int ArmorPoints => 1;
}
