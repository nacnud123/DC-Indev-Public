using VoxelEngine.Rendering;
using VoxelEngine.Utils;

namespace VoxelEngine.Items;

public class ItemGoldHelmet : ItemArmor
{
    public override ItemType Type => ItemType.GoldHelmet;
    public override string Name => "Gold Helmet";
    public override TextureCoords ItemCoords => UvHelper.FromTileCoords(3, 11);
    public override ArmorSlot? ArmorSlot => Items.ArmorSlot.Head;
    public override ArmorTier? ArmorTier => Items.ArmorTier.Gold;
    public override int MaxDurability => 78;
    public override int ArmorPoints => 2;
}
