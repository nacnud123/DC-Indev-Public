using VoxelEngine.Rendering;
using VoxelEngine.Utils;

namespace VoxelEngine.Items;

public class ItemGoldBar : Item
{
    public override ItemType Type => ItemType.GoldBar;
    public override string Name => "Gold Bar";
    public override TextureCoords ItemCoords => UvHelper.FromTileCoords(3, 1);
    public override int MaxStackSize => 64;
}
