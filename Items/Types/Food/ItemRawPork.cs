using VoxelEngine.Rendering;
using VoxelEngine.Utils;

namespace VoxelEngine.Items;

public class ItemRawPork : ItemFood
{
    public override ItemType Type => ItemType.RawPork;
    public override string Name => "Raw Pork";
    public override TextureCoords ItemCoords => UvHelper.FromTileCoords(7, 7);
    public override int FoodRestore => 3;
}
