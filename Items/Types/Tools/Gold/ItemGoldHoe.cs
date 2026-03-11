using VoxelEngine.Rendering;
using VoxelEngine.Utils;

namespace VoxelEngine.Items;

public class ItemGoldHoe : ItemHoe
{
    public override ItemType Type => ItemType.GoldHoe;
    public override string Name => "Gold Hoe";
    public override ToolTier ToolTier => ToolTier.Gold;
    public override TextureCoords ItemCoords => UvHelper.FromTileCoords(4, 4);
}
