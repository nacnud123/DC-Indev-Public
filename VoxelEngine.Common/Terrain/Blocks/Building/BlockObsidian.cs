using VoxelEngine.Items;
using VoxelEngine.Rendering;
using VoxelEngine.Utils;

namespace VoxelEngine.Terrain.Blocks;

/// <summary>Extremely tough building block; requires a diamond pickaxe, the highest tool tier in the
/// game, matching its very high hardness value.</summary>
public class BlockObsidian : Block
{
    public override BlockType Type => BlockType.Obsidian;
    public override string Name => "Obsidian";
    public override float Hardness => 10f; // Much higher than typical stone (1-5) - very slow to mine.
    public override BlockBreakMaterial BreakMaterial => BlockBreakMaterial.Stone;
    public override ToolType PreferredTool => ToolType.Pickaxe;
    public override ToolTier MinimumTier => ToolTier.Diamond;

    public override TextureCoords TopTextureCoords => UvHelper.FromTileCoords(0, 5);
    public override TextureCoords BottomTextureCoords => TopTextureCoords;
    public override TextureCoords SideTextureCoords => TopTextureCoords;
}
