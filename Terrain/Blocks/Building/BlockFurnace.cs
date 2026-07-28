using VoxelEngine.Items;
using VoxelEngine.Rendering;
using VoxelEngine.Utils;

namespace VoxelEngine.Terrain.Blocks;

/// <summary>Unlit (idle) furnace. Its smelting slots/progress are stored separately as a FurnaceData
/// in BlockEntityManager, keyed by world position. When smelting starts, the world swaps this block
/// for BlockFurnaceLit (see BlockFurnaceLit) to change the texture/light emission without losing the
/// FurnaceData at that position.</summary>
public class BlockFurnace : Block
{
    public override BlockType Type => BlockType.Furnace;
    public override string Name => "Furnace";
    public override float Hardness => 3.5f;
    public override BlockBreakMaterial BreakMaterial => BlockBreakMaterial.Stone;
    public override ToolType PreferredTool => ToolType.Pickaxe;
    public override ToolTier MinimumTier => ToolTier.Wood;

    public override TextureCoords TopTextureCoords => UvHelper.FromTileCoords(7, 2);
    public override TextureCoords BottomTextureCoords => UvHelper.FromTileCoords(7, 2);
    public override TextureCoords SideTextureCoords => UvHelper.FromTileCoords(7, 2);
    public override TextureCoords FrontTextureCoords => UvHelper.FromTileCoords(0, 6);
}
