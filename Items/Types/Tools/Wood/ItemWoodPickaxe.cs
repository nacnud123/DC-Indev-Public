using VoxelEngine.Rendering;
using VoxelEngine.Utils;

namespace VoxelEngine.Items;

/// <summary>Wood-tier pickaxe; lowest mining speed/attack, cheapest to craft, least durable of the "real" tiers.</summary>
public class ItemWoodPickaxe : ItemPickaxe
{
    public override ItemType Type => ItemType.WoodPickaxe;
    public override string Name => "Wooden Pickaxe";
    public override ToolTier ToolTier => ToolTier.Wood;
    public override TextureCoords ItemCoords => UvHelper.FromTileCoords(0, 7);
}
