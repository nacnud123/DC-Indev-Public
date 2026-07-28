using VoxelEngine.Items;
using VoxelEngine.Rendering;
using VoxelEngine.Utils;

namespace VoxelEngine.Terrain.Blocks;

/// <summary>Storage block made of 9 iron ingots; requires a stone (or better) pickaxe to mine.</summary>
public class BlockIronBlock : Block
{
    public override BlockType Type => BlockType.IronBlock;
    public override string Name => "Iron Block";
    public override float Hardness => 5f;
    public override BlockBreakMaterial BreakMaterial => BlockBreakMaterial.Stone;
    public override ToolType PreferredTool => ToolType.Pickaxe;
    public override ToolTier MinimumTier => ToolTier.Stone;

    public override TextureCoords TopTextureCoords => UvHelper.FromTileCoords(4, 5);
    public override TextureCoords BottomTextureCoords => TopTextureCoords;
    public override TextureCoords SideTextureCoords => TopTextureCoords;
}
