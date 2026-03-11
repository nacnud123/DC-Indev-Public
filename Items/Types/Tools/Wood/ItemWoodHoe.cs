using VoxelEngine.Rendering;
using VoxelEngine.Utils;

namespace VoxelEngine.Items;

public class ItemWoodHoe : ItemHoe
{
    public override ItemType Type => ItemType.WoodHoe;
    public override string Name => "Wooden Hoe";
    public override ToolTier ToolTier => ToolTier.Wood;
    public override TextureCoords ItemCoords => UvHelper.FromTileCoords(4, 7);
}
