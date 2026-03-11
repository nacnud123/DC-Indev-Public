using VoxelEngine.Rendering;
using VoxelEngine.Utils;

namespace VoxelEngine.Items;

public class ItemStick : Item
{
    public override ItemType Type => ItemType.Stick;
    public override string Name => "Stick";
    public override TextureCoords ItemCoords => UvHelper.FromTileCoords(0, 0);
    public override int MaxStackSize => 64;
}
