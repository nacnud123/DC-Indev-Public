using VoxelEngine.Rendering;
using VoxelEngine.Utils;

namespace VoxelEngine.Items;

/// <summary>Wood-tier hoe; behaves identically to every other hoe tier (tilling), differs from other wood tools only in durability.</summary>
public class ItemWoodHoe : ItemHoe
{
    public override ItemType Type => ItemType.WoodHoe;
    public override string Name => "Wooden Hoe";
    public override ToolTier ToolTier => ToolTier.Wood;
    public override TextureCoords ItemCoords => UvHelper.FromTileCoords(4, 7);
}
