using VoxelEngine.Rendering;
using VoxelEngine.Utils;

namespace VoxelEngine.Items;

/// <summary>Stone-tier shovel; a step up from wood in mining speed/attack and durability, still cheap to craft.</summary>
public class ItemStoneShovel : ItemShovel
{
    public override ItemType Type => ItemType.StoneShovel;
    public override string Name => "Stone Shovel";
    public override ToolTier ToolTier => ToolTier.Stone;
    public override TextureCoords ItemCoords => UvHelper.FromTileCoords(3, 6);
}
