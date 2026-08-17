
using VoxelEngine.Items;
using VoxelEngine.Rendering;
using VoxelEngine.Utils;

namespace VoxelEngine.Terrain.Blocks;

/// <summary>
/// Stone stair block. Solid, full-hardness stone variant that uses the
/// <see cref="RenderingType.Stair"/> mesh (a partial-cube stair shape) instead of a plain
/// cube. Note: unlike BlockTorch/BlockWallTorch this class does not itself read facing
/// metadata - any per-placement rotation/orientation of the stair mesh is resolved
/// elsewhere (mesh builder / placement code) using the block's stored metadata byte,
/// not by logic in this file.
/// </summary>
public class BlockStoneStair : Block
{
    public override BlockType Type => BlockType.StoneStair;
    public override string Name => "Stone Stair";
    public override BlockBreakMaterial BreakMaterial => BlockBreakMaterial.Stone;
    public override ToolType PreferredTool => ToolType.Pickaxe;
    public override ToolTier MinimumTier => ToolTier.Wood;
    public override RenderingType RenderType => RenderingType.Stair;

    public override float Hardness => 1.5f;
    // Stair mesh leaves part of the cube empty, so it's flagged transparent for culling
    // purposes even though it doesn't let light pass differently (LightOpacity stays 0).
    public override bool IsTransparent => true;
    public override int LightOpacity => 0;
    public override bool IsSolid => true;
    public override bool ShowInInventory => true;

    // Full-cube collision bounds even though the stair mesh is a partial shape -
    // collision uses the AABB, not the actual stair geometry.
    public override Vector3 BoundsMax => Vector3.One;

    // Reuses the plain stone tile for all faces of the stair mesh.
    public override TextureCoords TopTextureCoords => UvHelper.FromTileCoords(0, 0);
    public override TextureCoords BottomTextureCoords => TopTextureCoords;
    public override TextureCoords SideTextureCoords => TopTextureCoords;
}
