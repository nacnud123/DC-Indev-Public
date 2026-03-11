using VoxelEngine.Rendering;
using VoxelEngine.Utils;

namespace VoxelEngine.Items;

public class ItemStoneShovel : ItemShovel
{
    public override ItemType Type => ItemType.StoneShovel;
    public override string Name => "Stone Shovel";
    public override ToolTier ToolTier => ToolTier.Stone;
    public override TextureCoords ItemCoords => UvHelper.FromTileCoords(3, 6);
}
