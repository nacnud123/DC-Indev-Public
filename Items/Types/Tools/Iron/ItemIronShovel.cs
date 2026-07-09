using VoxelEngine.Rendering;
using VoxelEngine.Utils;

namespace VoxelEngine.Items;

/// <summary>Iron-tier shovel; solid mid-game mining speed/attack with good durability.</summary>
public class ItemIronShovel : ItemShovel
{
    public override ItemType Type => ItemType.IronShovel;
    public override string Name => "Iron Shovel";
    public override ToolTier ToolTier => ToolTier.Iron;
    public override TextureCoords ItemCoords => UvHelper.FromTileCoords(3, 5);
}
