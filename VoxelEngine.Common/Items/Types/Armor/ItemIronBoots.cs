using VoxelEngine.Rendering;
using VoxelEngine.Utils;

namespace VoxelEngine.Items;

/// <summary>Iron-tier boots (Feet slot); grants 2 armor points.</summary>
public class ItemIronBoots : ItemArmor
{
    public override ItemType Type => ItemType.IronBoots;
    public override string Name => "Iron Boots";
    public override TextureCoords ItemCoords => UvHelper.FromTileCoords(2, 8);
    public override ArmorSlot? ArmorSlot => Items.ArmorSlot.Feet;
    public override ArmorTier? ArmorTier => Items.ArmorTier.Iron;
    public override int MaxDurability => 196;
    public override int ArmorPoints => 2;
}
