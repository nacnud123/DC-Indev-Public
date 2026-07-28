using VoxelEngine.Rendering;
using VoxelEngine.Utils;

namespace VoxelEngine.Items;

public class ItemStew : ItemFood
{
    public override ItemType Type => ItemType.Stew;
    public override string Name => "Mushroom Stew";
    public override TextureCoords ItemCoords => UvHelper.FromTileCoords(8, 2);
    public override int FoodRestore => 6;
    public override int MaxStackSize => 1;
}
