using VoxelEngine.Items;
using VoxelEngine.Rendering;
using VoxelEngine.Utils;

namespace VoxelEngine.Terrain.Blocks;

/// <summary>
/// Full-height, full-collision block that a placed BlockStoneSlab becomes when a second slab
/// is stacked into the same cell (the two half-slabs merge into one solid block instead of
/// stacking as two separate half-height entities). Unlike BlockStoneSlab it uses the default
/// (full-cube) RenderType/Bounds from the Block base class - no partial AABB or slab-specific
/// rendering. It has its own BlockType (DoubleStoneslab) rather than reusing regular Stone, and
/// is hidden from the inventory since it's only ever produced by combining two slabs, never
/// placed directly. The actual "combine two slabs into this block" logic lives in the
/// placement/interaction code, not in this class.
/// </summary>
public class BlockDoubleStoneSlab : Block
{
    public override BlockType Type => BlockType.DoubleStoneslab;
    public override string Name => "Double Stone Slab";
    public override BlockBreakMaterial BreakMaterial => BlockBreakMaterial.Stone;
    public override ToolType PreferredTool => ToolType.Pickaxe;
    public override ToolTier MinimumTier => ToolTier.Wood;
    // Never appears as a selectable inventory item - only created by merging two half-slabs.
    public override bool ShowInInventory => false;

    public override TextureCoords TopTextureCoords => UvHelper.FromTileCoords(4, 0);
    public override TextureCoords BottomTextureCoords => TopTextureCoords;
    public override TextureCoords SideTextureCoords => TopTextureCoords;
}
