using VoxelEngine.Items;
using VoxelEngine.Rendering;
using VoxelEngine.Utils;

namespace VoxelEngine.Terrain.Blocks;

/// <summary>Decorative wooden bookshelf. Wood-tier hardness/tool preference and flammable like other
/// wooden furniture blocks; has a distinct spine texture on its top/bottom vs. its sides.</summary>
public class BlockBookcase : Block
{
    public override BlockType Type => BlockType.Bookcase;
    public override string Name => "Bookcase";
    public override float Hardness => 1.5f;
    public override BlockBreakMaterial BreakMaterial => BlockBreakMaterial.Wooden;
    public override ToolType PreferredTool => ToolType.Axe;
    public override bool IsFlamable => true;

    public override TextureCoords TopTextureCoords => UvHelper.FromTileCoords(2, 2);
    public override TextureCoords BottomTextureCoords => TopTextureCoords;
    public override TextureCoords SideTextureCoords => UvHelper.FromTileCoords(1, 5);
}
