using VoxelEngine.Items;
using VoxelEngine.Rendering;
using VoxelEngine.Utils;

namespace VoxelEngine.Terrain.Blocks;

/// <summary>
/// Ore variant of stone that yields Diamond items when mined. Requires an Iron-tier (or
/// better) pickaxe to actually get a drop - mining it with a lower tier tool destroys the
/// block without yielding anything, gating diamonds behind the iron-tool progression.
/// </summary>
public class BlockDiamondOre : Block
{
    public override BlockType Type => BlockType.DiamondOre;
    public override string Name => "Diamond Ore";
    public override float Hardness => 2.0f;
    public override BlockBreakMaterial BreakMaterial => BlockBreakMaterial.Stone;
    public override ToolType PreferredTool => ToolType.Pickaxe;
    // Highest ordinary tool-tier gate in the game - only Iron (or higher) pickaxes drop diamonds.
    public override ToolTier MinimumTier => ToolTier.Iron;
    // Overrides the default "drop self as block" behavior: breaking this block yields a
    // Diamond item, not a DiamondOre block.
    public override ItemStack? GetDrop(byte metadata) => ItemStack.FromItem(ItemType.Diamond);

    // Atlas tile (2,4); same texture used for all six faces.
    public override TextureCoords TopTextureCoords => UvHelper.FromTileCoords(2, 4);
    public override TextureCoords BottomTextureCoords => TopTextureCoords;
    public override TextureCoords SideTextureCoords => TopTextureCoords;
}
