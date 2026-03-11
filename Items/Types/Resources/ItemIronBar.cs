using VoxelEngine.Rendering;
using VoxelEngine.Utils;

namespace VoxelEngine.Items;

public class ItemIronBar : Item
{
    public override ItemType Type => ItemType.IronBar;
    public override string Name => "Iron Bar";
    public override TextureCoords ItemCoords => UvHelper.FromTileCoords(2, 1);
    public override int MaxStackSize => 64;
}
