using VoxelEngine.Items;
using VoxelEngine.Rendering;
using VoxelEngine.Utils;

namespace VoxelEngine.Terrain.Blocks;

/// <summary>
/// Decorative cosmetic variant of Cobblestone (typically found in dungeon/structure generation).
/// Behaviorally identical to Cobblestone aside from its own texture; uses the default
/// Block.GetDrop, so it drops itself when mined.
/// </summary>
public class BlockMossyCobblestone: Block
{
    public override BlockType Type => BlockType.MossyCobblestone;
    public override string Name => "Mossy Cobblestone";
    public override BlockBreakMaterial BreakMaterial => BlockBreakMaterial.Stone;
    public override ToolType PreferredTool => ToolType.Pickaxe;
    public override ToolTier MinimumTier => ToolTier.Wood;

    public override float Hardness => 1.5f;

    public override TextureCoords TopTextureCoords => UvHelper.FromTileCoords(7, 3);
    public override TextureCoords BottomTextureCoords => TopTextureCoords;
    public override TextureCoords SideTextureCoords => TopTextureCoords;
}


