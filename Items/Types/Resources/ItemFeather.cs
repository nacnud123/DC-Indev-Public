using VoxelEngine.Rendering;
using VoxelEngine.Utils;

namespace VoxelEngine.Items;

public class ItemFeather : Item
{
    public override ItemType Type => ItemType.Feather;
    public override string Name => "Feather";
    public override TextureCoords ItemCoords => UvHelper.FromTileCoords(5, 5);
    public override int MaxStackSize => 64;
}
