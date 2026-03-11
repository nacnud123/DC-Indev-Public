using VoxelEngine.Rendering;
using VoxelEngine.Utils;

namespace VoxelEngine.Items;

public class ItemCoal : Item
{
    public override ItemType Type => ItemType.Coal;
    public override string Name => "Coal";
    public override TextureCoords ItemCoords => UvHelper.FromTileCoords(1, 1);
    public override int MaxStackSize => 64;
}
