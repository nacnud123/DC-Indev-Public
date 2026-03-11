using VoxelEngine.Rendering;
using VoxelEngine.Utils;

namespace VoxelEngine.Items;

public class ItemString : Item
{
    public override ItemType Type => ItemType.String;
    public override string Name => "String";
    public override TextureCoords ItemCoords => UvHelper.FromTileCoords(5, 4);
    public override int MaxStackSize => 64;
}
