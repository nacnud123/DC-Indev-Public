using VoxelEngine.Rendering;
using VoxelEngine.Utils;

namespace VoxelEngine.Items;

public class ItemArrow : Item
{
    public override ItemType Type => ItemType.Arrow;
    public override string Name => "Arrow";
    public override TextureCoords ItemCoords => UvHelper.FromTileCoords(1, 2);
    public override int MaxStackSize => 64;
}
