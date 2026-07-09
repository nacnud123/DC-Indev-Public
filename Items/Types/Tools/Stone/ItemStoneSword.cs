using VoxelEngine.Rendering;
using VoxelEngine.Utils;

namespace VoxelEngine.Items;

/// <summary>Stone-tier sword; a step up from wood in mining speed/attack and durability, still cheap to craft.</summary>
public class ItemStoneSword : ItemSword
{
    public override ItemType Type => ItemType.StoneSword;
    public override string Name => "Stone Sword";
    public override ToolTier ToolTier => ToolTier.Stone;
    public override TextureCoords ItemCoords => UvHelper.FromTileCoords(1, 6);
}
