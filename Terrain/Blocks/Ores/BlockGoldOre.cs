using VoxelEngine.Items;
using VoxelEngine.Rendering;
using VoxelEngine.Utils;

namespace VoxelEngine.Terrain.Blocks;

public class BlockGoldOre : Block
{
    public override BlockType Type => BlockType.GoldOre;
    public override string Name => "Gold Ore";
    public override float Hardness => 2.0f;
    public override BlockBreakMaterial BreakMaterial => BlockBreakMaterial.Stone;
    public override ToolType PreferredTool => ToolType.Pickaxe;
    public override ToolTier MinimumTier => ToolTier.Iron;

    public override TextureCoords TopTextureCoords => UvHelper.FromTileCoords(1, 4);
    public override TextureCoords BottomTextureCoords => TopTextureCoords;
    public override TextureCoords SideTextureCoords => TopTextureCoords;
}
