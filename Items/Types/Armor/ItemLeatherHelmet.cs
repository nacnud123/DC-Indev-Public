using VoxelEngine.Rendering;
using VoxelEngine.Utils;

namespace VoxelEngine.Items;

/// <summary>Leather-tier helmet (Head slot); lowest tier, grants 1 armor point.</summary>
public class ItemLeatherHelmet : ItemArmor
{
    public override ItemType Type => ItemType.LeatherHelmet;
    public override string Name => "Leather Helmet";
    public override TextureCoords ItemCoords => UvHelper.FromTileCoords(0, 11);
    public override ArmorSlot? ArmorSlot => Items.ArmorSlot.Head;
    public override ArmorTier? ArmorTier => Items.ArmorTier.Leather;
    public override int MaxDurability => 56;
    public override int ArmorPoints => 1;
}
