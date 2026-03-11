using VoxelEngine.Rendering;
using VoxelEngine.Utils;

namespace VoxelEngine.Items;

public class ItemWheat : Item
{
    public override ItemType Type => ItemType.Wheat;
    public override string Name => "Wheat";
    public override TextureCoords ItemCoords => UvHelper.FromTileCoords(7, 3);
    public override int MaxStackSize => 64;
}
