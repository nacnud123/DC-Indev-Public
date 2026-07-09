using VoxelEngine.Rendering;
using VoxelEngine.Utils;

namespace VoxelEngine.Items;

/// <summary>Iron-tier sword; solid mid-game mining speed/attack with good durability.</summary>
public class ItemIronSword : ItemSword
{
    public override ItemType Type => ItemType.IronSword;
    public override string Name => "Iron Sword";
    public override ToolTier ToolTier => ToolTier.Iron;
    public override TextureCoords ItemCoords => UvHelper.FromTileCoords(1, 5);
}
