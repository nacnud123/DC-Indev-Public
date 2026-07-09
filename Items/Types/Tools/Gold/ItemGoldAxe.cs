using VoxelEngine.Rendering;
using VoxelEngine.Utils;

namespace VoxelEngine.Items;

/// <summary>Gold-tier axe; highest mining speed of any tier but very low durability (fragile), moderate attack.</summary>
public class ItemGoldAxe : ItemAxe
{
    public override ItemType Type => ItemType.GoldAxe;
    public override string Name => "Gold Axe";
    public override ToolTier ToolTier => ToolTier.Gold;
    public override TextureCoords ItemCoords => UvHelper.FromTileCoords(2, 4);
}
