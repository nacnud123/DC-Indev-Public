using VoxelEngine.Rendering;
using VoxelEngine.Utils;

namespace VoxelEngine.Items;

/// <summary>Wood-tier shovel; lowest mining speed/attack, cheapest to craft, least durable of the "real" tiers.</summary>
public class ItemWoodShovel : ItemShovel
{
    public override ItemType Type => ItemType.WoodShovel;
    public override string Name => "Wooden Shovel";
    public override ToolTier ToolTier => ToolTier.Wood;
    public override TextureCoords ItemCoords => UvHelper.FromTileCoords(3, 7);
}
