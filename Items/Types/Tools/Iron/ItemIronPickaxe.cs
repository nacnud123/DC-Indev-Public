using VoxelEngine.Rendering;
using VoxelEngine.Utils;

namespace VoxelEngine.Items;

public class ItemIronPickaxe : ItemPickaxe
{
    public override ItemType Type => ItemType.IronPickaxe;
    public override string Name => "Iron Pickaxe";
    public override ToolTier ToolTier => ToolTier.Iron;
    public override TextureCoords ItemCoords => UvHelper.FromTileCoords(0, 5);
}
