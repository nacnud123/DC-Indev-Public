using VoxelEngine.Items;
using VoxelEngine.Rendering;
using VoxelEngine.Utils;

namespace VoxelEngine.Terrain.Blocks;

/// <summary>Basic decorative stone-tier building block (uniform texture on all faces); requires at
/// least a wood pickaxe to drop.</summary>
public class BlockBricks : Block
{
    public override BlockType Type => BlockType.Bricks;
    public override string Name => "Bricks";
    public override float Hardness => 1.5f;
    public override BlockBreakMaterial BreakMaterial => BlockBreakMaterial.Stone;
    public override ToolType PreferredTool => ToolType.Pickaxe;
    public override ToolTier MinimumTier => ToolTier.Wood;

    public override TextureCoords TopTextureCoords => UvHelper.FromTileCoords(7, 0);
    public override TextureCoords BottomTextureCoords => TopTextureCoords;
    public override TextureCoords SideTextureCoords => TopTextureCoords;
}
