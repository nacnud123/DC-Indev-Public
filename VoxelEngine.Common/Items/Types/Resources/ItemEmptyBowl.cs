using VoxelEngine.Rendering;
using VoxelEngine.Utils;

namespace VoxelEngine.Items;

public class ItemEmptyBowl : Item
{
    public override ItemType Type => ItemType.EmptyBowl;
    public override string Name => "Empty Bowl";
    public override TextureCoords ItemCoords => UvHelper.FromTileCoords(7, 2);
    public override int MaxStackSize => 64;
}
