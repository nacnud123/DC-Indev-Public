using VoxelEngine.Items;
using VoxelEngine.Rendering;
using VoxelEngine.Utils;

namespace VoxelEngine.Terrain.Blocks;

/// <summary>
/// Standard crafting/building material dropped when BlockStone is mined (see
/// BlockStone.GetDrop). Uses default Block.GetDrop, so mining a placed Cobblestone block
/// simply drops itself.
/// </summary>
public class BlockCobblestone : Block
{
    public override BlockType Type => BlockType.CobbleStone;
    public override string Name => "Cobblestone";
    public override BlockBreakMaterial BreakMaterial => BlockBreakMaterial.Stone;
    public override ToolType PreferredTool => ToolType.Pickaxe;
    public override ToolTier MinimumTier => ToolTier.Wood;

    public override float Hardness => 1.5f;

    public override TextureCoords TopTextureCoords => UvHelper.FromTileCoords(7, 2);
    public override TextureCoords BottomTextureCoords => TopTextureCoords;
    public override TextureCoords SideTextureCoords => TopTextureCoords;
}
