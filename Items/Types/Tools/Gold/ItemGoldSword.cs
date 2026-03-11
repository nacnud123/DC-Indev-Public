using VoxelEngine.Rendering;
using VoxelEngine.Utils;

namespace VoxelEngine.Items;

public class ItemGoldSword : ItemSword
{
    public override ItemType Type => ItemType.GoldSword;
    public override string Name => "Gold Sword";
    public override ToolTier ToolTier => ToolTier.Gold;
    public override TextureCoords ItemCoords => UvHelper.FromTileCoords(1, 4);
}
