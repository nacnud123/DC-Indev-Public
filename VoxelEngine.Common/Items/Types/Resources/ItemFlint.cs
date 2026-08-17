using VoxelEngine.Rendering;
using VoxelEngine.Utils;

namespace VoxelEngine.Items;

public class ItemFlint : Item
{
    public override ItemType Type => ItemType.Flint;
    public override string Name => "Flint";
    public override TextureCoords ItemCoords => UvHelper.FromTileCoords(5, 1);
    public override int MaxStackSize => 64;
}
