using VoxelEngine.Rendering;
using VoxelEngine.Utils;

namespace VoxelEngine.Items;

/// <summary>Wood-tier sword; lowest attack damage, cheapest to craft, least durable of the "real" tiers.</summary>
public class ItemWoodSword : ItemSword
{
    public override ItemType Type => ItemType.WoodSword;
    public override string Name => "Wooden Sword";
    public override ToolTier ToolTier => ToolTier.Wood;
    public override TextureCoords ItemCoords => UvHelper.FromTileCoords(1, 7);
}
