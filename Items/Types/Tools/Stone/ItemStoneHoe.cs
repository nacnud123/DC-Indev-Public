using VoxelEngine.Rendering;
using VoxelEngine.Utils;

namespace VoxelEngine.Items;

/// <summary>Stone-tier hoe; behaves identically to every other hoe tier (tilling), differs only in durability.</summary>
public class ItemStoneHoe : ItemHoe
{
    public override ItemType Type => ItemType.StoneHoe;
    public override string Name => "Stone Hoe";
    public override ToolTier ToolTier => ToolTier.Stone;
    public override TextureCoords ItemCoords => UvHelper.FromTileCoords(4, 6);
}
