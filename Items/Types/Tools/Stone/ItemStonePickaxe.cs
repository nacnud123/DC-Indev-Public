using VoxelEngine.Rendering;
using VoxelEngine.Utils;

namespace VoxelEngine.Items;

public class ItemStonePickaxe : ItemPickaxe
{
    public override ItemType Type => ItemType.StonePickaxe;
    public override string Name => "Stone Pickaxe";
    public override ToolTier ToolTier => ToolTier.Stone;
    public override TextureCoords ItemCoords => UvHelper.FromTileCoords(0, 6);
}
