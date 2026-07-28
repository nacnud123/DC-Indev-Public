using VoxelEngine.Rendering;
using VoxelEngine.Utils;

namespace VoxelEngine.Items;

/// <summary>Leather-tier boots (Feet slot); lowest tier, grants 1 armor point.</summary>
public class ItemLeatherBoots : ItemArmor
{
    public override ItemType Type => ItemType.LeatherBoots;
    public override string Name => "Leather Boots";
    public override TextureCoords ItemCoords => UvHelper.FromTileCoords(0, 8);
    public override ArmorSlot? ArmorSlot => Items.ArmorSlot.Feet;
    public override ArmorTier? ArmorTier => Items.ArmorTier.Leather;
    public override int MaxDurability => 66;
    public override int ArmorPoints => 1;
}
