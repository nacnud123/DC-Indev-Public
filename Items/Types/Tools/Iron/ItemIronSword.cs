using VoxelEngine.Rendering;
using VoxelEngine.Utils;

namespace VoxelEngine.Items;

public class ItemIronSword : ItemSword
{
    public override ItemType Type => ItemType.IronSword;
    public override string Name => "Iron Sword";
    public override ToolTier ToolTier => ToolTier.Iron;
    public override TextureCoords ItemCoords => UvHelper.FromTileCoords(1, 5);
}
