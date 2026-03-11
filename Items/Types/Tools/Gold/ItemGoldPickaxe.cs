using VoxelEngine.Rendering;
using VoxelEngine.Utils;

namespace VoxelEngine.Items;

public class ItemGoldPickaxe : ItemPickaxe
{
    public override ItemType Type => ItemType.GoldPickaxe;
    public override string Name => "Gold Pickaxe";
    public override ToolTier ToolTier => ToolTier.Gold;
    public override TextureCoords ItemCoords => UvHelper.FromTileCoords(0, 4);
}
