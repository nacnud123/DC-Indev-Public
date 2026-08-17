using VoxelEngine.Rendering;
using VoxelEngine.Utils;

namespace VoxelEngine.Items;

public class ItemBone : Item
{
    public override ItemType Type => ItemType.Bone;
    public override string Name => "Bone";
    public override TextureCoords ItemCoords => UvHelper.FromTileCoords(5, 6);
    public override int MaxStackSize => 64;
}
