using VoxelEngine.Rendering;
using VoxelEngine.Utils;

namespace VoxelEngine.Items;

public class ItemDiamondAxe : ItemAxe
{
    public override ItemType Type => ItemType.DiamondAxe;
    public override string Name => "Diamond Axe";
    public override ToolTier ToolTier => ToolTier.Diamond;
    public override TextureCoords ItemCoords => UvHelper.FromTileCoords(2, 3);
}
