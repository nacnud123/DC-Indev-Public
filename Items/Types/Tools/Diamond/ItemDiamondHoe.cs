using VoxelEngine.Rendering;
using VoxelEngine.Utils;

namespace VoxelEngine.Items;

public class ItemDiamondHoe : ItemHoe
{
    public override ItemType Type => ItemType.DiamondHoe;
    public override string Name => "Diamond Hoe";
    public override ToolTier ToolTier => ToolTier.Diamond;
    public override TextureCoords ItemCoords => UvHelper.FromTileCoords(4, 3);
}
