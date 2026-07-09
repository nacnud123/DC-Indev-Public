using VoxelEngine.Rendering;
using VoxelEngine.Utils;

namespace VoxelEngine.Items;

/// <summary>Iron-tier axe; solid mid-game mining speed/attack with good durability.</summary>
public class ItemIronAxe : ItemAxe
{
    public override ItemType Type => ItemType.IronAxe;
    public override string Name => "Iron Axe";
    public override ToolTier ToolTier => ToolTier.Iron;
    public override TextureCoords ItemCoords => UvHelper.FromTileCoords(2, 5);
}
