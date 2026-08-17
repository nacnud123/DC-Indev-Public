using VoxelEngine.Items;
using VoxelEngine.Rendering;
using VoxelEngine.Utils;

namespace VoxelEngine.Terrain.Blocks;

/// <summary>Storage block made of 9 diamonds; requires an iron (or better) pickaxe, reflecting its
/// high value/rarity.</summary>
public class BlockDiamondBlock : Block
{
    public override BlockType Type => BlockType.DiamondBlock;
    public override string Name => "Diamond Block";
    public override float Hardness => 5f;
    public override BlockBreakMaterial BreakMaterial => BlockBreakMaterial.Stone;
    public override ToolType PreferredTool => ToolType.Pickaxe;
    public override ToolTier MinimumTier => ToolTier.Iron;

    public override TextureCoords TopTextureCoords => UvHelper.FromTileCoords(2, 5);
    public override TextureCoords BottomTextureCoords => TopTextureCoords;
    public override TextureCoords SideTextureCoords => TopTextureCoords;
}
