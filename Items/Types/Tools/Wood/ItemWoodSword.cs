using VoxelEngine.Rendering;
using VoxelEngine.Utils;

namespace VoxelEngine.Items;

public class ItemWoodSword : ItemSword
{
    public override ItemType Type => ItemType.WoodSword;
    public override string Name => "Wooden Sword";
    public override ToolTier ToolTier => ToolTier.Wood;
    public override TextureCoords ItemCoords => UvHelper.FromTileCoords(1, 7);
}
