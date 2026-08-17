using VoxelEngine.Rendering;
using VoxelEngine.Utils;

namespace VoxelEngine.Items;

public class ItemDiamond : Item
{
    public override ItemType Type => ItemType.Diamond;
    public override string Name => "Diamond";
    public override TextureCoords ItemCoords => UvHelper.FromTileCoords(0, 1);
    public override int MaxStackSize => 64;
}
