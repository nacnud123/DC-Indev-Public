using VoxelEngine.Rendering;
using VoxelEngine.Utils;

namespace VoxelEngine.Items;

/// <summary>Diamond-tier shovel; best attack damage and by far the highest durability of any tier.</summary>
public class ItemDiamondShovel : ItemShovel
{
    public override ItemType Type => ItemType.DiamondShovel;
    public override string Name => "Diamond Shovel";
    public override ToolTier ToolTier => ToolTier.Diamond;
    public override TextureCoords ItemCoords => UvHelper.FromTileCoords(3, 3);
}
