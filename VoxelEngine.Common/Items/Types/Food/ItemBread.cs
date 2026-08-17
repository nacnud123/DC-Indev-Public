using VoxelEngine.Rendering;
using VoxelEngine.Utils;

namespace VoxelEngine.Items;

public class ItemBread : ItemFood
{
    public override ItemType Type => ItemType.Bread;
    public override string Name => "Bread";
    public override TextureCoords ItemCoords => UvHelper.FromTileCoords(7, 4);
    public override int FoodRestore => 5;
}
