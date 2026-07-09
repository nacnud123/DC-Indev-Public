using VoxelEngine.Rendering;
using VoxelEngine.Utils;

namespace VoxelEngine.Items;

/// <summary>Diamond-tier sword; best attack damage and by far the highest durability of any tier.</summary>
public class ItemDiamondSword : ItemSword
{
    public override ItemType Type => ItemType.DiamondSword;
    public override string Name => "Diamond Sword";
    public override ToolTier ToolTier => ToolTier.Diamond;
    public override TextureCoords ItemCoords => UvHelper.FromTileCoords(1, 3);
}
