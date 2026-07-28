using VoxelEngine.Rendering;
using VoxelEngine.Utils;

namespace VoxelEngine.Items;

/// <summary>Iron-tier hoe; behaves identically to every other hoe tier (tilling), differs only in durability.</summary>
public class ItemIronHoe : ItemHoe
{
    public override ItemType Type => ItemType.IronHoe;
    public override string Name => "Iron Hoe";
    public override ToolTier ToolTier => ToolTier.Iron;
    public override TextureCoords ItemCoords => UvHelper.FromTileCoords(4, 5);
}
