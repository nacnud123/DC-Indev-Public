using VoxelEngine.Rendering;
using VoxelEngine.Utils;

namespace VoxelEngine.Items;

/// <summary>Stone-tier axe; a step up from wood in mining speed/attack and durability, still cheap to craft.</summary>
public class ItemStoneAxe : ItemAxe
{
    public override ItemType Type => ItemType.StoneAxe;
    public override string Name => "Stone Axe";
    public override ToolTier ToolTier => ToolTier.Stone;
    public override TextureCoords ItemCoords => UvHelper.FromTileCoords(2, 6);
}
