using VoxelEngine.Items;
using VoxelEngine.Rendering;
using VoxelEngine.Utils;

namespace VoxelEngine.Terrain.Blocks;

/// <summary>Storage block made of 9 gold ingots; requires an iron (or better) pickaxe to mine.</summary>
public class BlockGoldBlock : Block
{
    public override BlockType Type => BlockType.GoldBlock;
    public override string Name => "Gold Block";
    public override float Hardness => 3f;
    public override BlockBreakMaterial BreakMaterial => BlockBreakMaterial.Stone;
    public override ToolType PreferredTool => ToolType.Pickaxe;
    public override ToolTier MinimumTier => ToolTier.Iron;

    public override TextureCoords TopTextureCoords => UvHelper.FromTileCoords(3, 5);
    public override TextureCoords BottomTextureCoords => TopTextureCoords;
    public override TextureCoords SideTextureCoords => TopTextureCoords;
}
