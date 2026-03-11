using VoxelEngine.Rendering;
using VoxelEngine.Utils;

namespace VoxelEngine.Items;

public class ItemDiamondSword : ItemSword
{
    public override ItemType Type => ItemType.DiamondSword;
    public override string Name => "Diamond Sword";
    public override ToolTier ToolTier => ToolTier.Diamond;
    public override TextureCoords ItemCoords => UvHelper.FromTileCoords(1, 3);
}
