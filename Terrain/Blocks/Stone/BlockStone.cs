using VoxelEngine.Items;
using VoxelEngine.Rendering;
using VoxelEngine.Utils;

namespace VoxelEngine.Terrain.Blocks;

public class BlockStone : Block
{
    public override BlockType Type => BlockType.Stone;
    public override string Name => "Stone";
    public override BlockBreakMaterial BreakMaterial => BlockBreakMaterial.Stone;
    public override ToolType PreferredTool => ToolType.Pickaxe;
    public override ToolTier MinimumTier => ToolTier.Wood;

    public override float Hardness => 1.5f;
    public override ItemStack? GetDrop(byte metadata) => ItemStack.FromBlock(BlockType.CobbleStone);
    
    public override TextureCoords TopTextureCoords => UvHelper.FromTileCoords(0, 0);
    public override TextureCoords BottomTextureCoords => TopTextureCoords;
    public override TextureCoords SideTextureCoords => TopTextureCoords;
}
