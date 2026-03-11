using VoxelEngine.Rendering;
using VoxelEngine.Utils;

namespace VoxelEngine.Items;

public class ItemStoneAxe : ItemAxe
{
    public override ItemType Type => ItemType.StoneAxe;
    public override string Name => "Stone Axe";
    public override ToolTier ToolTier => ToolTier.Stone;
    public override TextureCoords ItemCoords => UvHelper.FromTileCoords(2, 6);
}
