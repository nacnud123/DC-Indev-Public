using VoxelEngine.Rendering;
using VoxelEngine.Utils;

namespace VoxelEngine.Items;

public class ItemSulfur : Item
{
    public override ItemType Type => ItemType.Sulfur;
    public override string Name => "Sulfur";
    public override TextureCoords ItemCoords => UvHelper.FromTileCoords(4, 1);
    public override int MaxStackSize => 64;
}
