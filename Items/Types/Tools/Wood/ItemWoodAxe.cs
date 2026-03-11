using VoxelEngine.Rendering;
using VoxelEngine.Utils;

namespace VoxelEngine.Items;

public class ItemWoodAxe : ItemAxe
{
    public override ItemType Type => ItemType.WoodAxe;
    public override string Name => "Wooden Axe";
    public override ToolTier ToolTier => ToolTier.Wood;
    public override TextureCoords ItemCoords => UvHelper.FromTileCoords(2, 7);
}
