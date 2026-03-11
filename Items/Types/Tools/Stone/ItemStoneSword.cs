using VoxelEngine.Rendering;
using VoxelEngine.Utils;

namespace VoxelEngine.Items;

public class ItemStoneSword : ItemSword
{
    public override ItemType Type => ItemType.StoneSword;
    public override string Name => "Stone Sword";
    public override ToolTier ToolTier => ToolTier.Stone;
    public override TextureCoords ItemCoords => UvHelper.FromTileCoords(1, 6);
}
