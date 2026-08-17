using VoxelEngine.Items;
using VoxelEngine.Rendering;
using VoxelEngine.Utils;

namespace VoxelEngine.Terrain.Blocks;

/// <summary>Crafting station block. Opens the crafting UI (CraftingRegistry-backed grid) on interaction;
/// this class only defines its physical block properties/textures, the crafting grid logic lives in
/// the UI/interaction layer, not here.</summary>
public class BlockWorkBench : Block
{
    public override BlockType Type => BlockType.WorkBench;
    public override string Name => "Workbench";
    public override float Hardness => 2.5f;
    public override BlockBreakMaterial BreakMaterial => BlockBreakMaterial.Wooden;
    public override ToolType PreferredTool => ToolType.Axe;
    public override bool IsFlamable => true;

    public override TextureCoords TopTextureCoords => UvHelper.FromTileCoords(2, 6);
    public override TextureCoords BottomTextureCoords => UvHelper.FromTileCoords(2, 2);
    public override TextureCoords SideTextureCoords => UvHelper.FromTileCoords(1, 6);
}
