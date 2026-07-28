using VoxelEngine.Rendering;
using VoxelEngine.Utils;

namespace VoxelEngine.Terrain.Blocks;

/// <summary>
/// Indestructible world-boundary block (used at/near the bottom of the map). Hardness of -1
/// combined with IsBreakable = false makes it immune to mining regardless of tool. Hidden from
/// the inventory since it is never meant to be a placeable item.
/// </summary>
public class BlockBedrock: Block
{
    public override BlockType Type => BlockType.Bedrock;
    public override string Name => "Bedrock";
    public override BlockBreakMaterial BreakMaterial => BlockBreakMaterial.Stone;
    // Negative hardness is a defensive/explicit "never breaks" marker on top of IsBreakable.
    public override float Hardness => -1f;
    // Cannot be mined by any tool/tier.
    public override bool IsBreakable => false;
    public override bool ShowInInventory => false;

    public override TextureCoords TopTextureCoords => UvHelper.FromTileCoords(5, 0);
    public override TextureCoords BottomTextureCoords => TopTextureCoords;
    public override TextureCoords SideTextureCoords => TopTextureCoords;
}