using VoxelEngine.Rendering;
using VoxelEngine.Utils;

namespace VoxelEngine.Items;

/// <summary>Gold-tier shovel; highest mining speed of any tier but very low durability (fragile), moderate attack.</summary>
public class ItemGoldShovel : ItemShovel
{
    public override ItemType Type => ItemType.GoldShovel;
    public override string Name => "Gold Shovel";
    public override ToolTier ToolTier => ToolTier.Gold;
    public override TextureCoords ItemCoords => UvHelper.FromTileCoords(3, 4);
}
