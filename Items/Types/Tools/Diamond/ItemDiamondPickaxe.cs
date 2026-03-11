using VoxelEngine.Rendering;
using VoxelEngine.Utils;

namespace VoxelEngine.Items;

public class ItemDiamondPickaxe : ItemPickaxe
{
    public override ItemType Type => ItemType.DiamondPickaxe;
    public override string Name => "Diamond Pickaxe";
    public override ToolTier ToolTier => ToolTier.Diamond;
    public override TextureCoords ItemCoords => UvHelper.FromTileCoords(0, 3);
}
