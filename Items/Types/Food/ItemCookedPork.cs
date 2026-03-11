using VoxelEngine.Rendering;
using VoxelEngine.Utils;

namespace VoxelEngine.Items;

public class ItemCookedPork : ItemFood
{
    public override ItemType Type => ItemType.CookedPork;
    public override string Name => "Cooked Pork";
    public override TextureCoords ItemCoords => UvHelper.FromTileCoords(7, 6);
    public override int FoodRestore => 8;
}
