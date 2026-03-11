using VoxelEngine.Rendering;
using VoxelEngine.Utils;

namespace VoxelEngine.Items;

public class ItemApple : ItemFood
{
    public override ItemType Type => ItemType.Apple;
    public override string Name => "Apple";
    public override TextureCoords ItemCoords => UvHelper.FromTileCoords(7, 5);
    public override int FoodRestore => 4;
}
