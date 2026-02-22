using VoxelEngine.Rendering;
using VoxelEngine.Utils;

namespace VoxelEngine.Terrain.Blocks;

public class BlockPlanks : Block
{
    public override BlockType Type => BlockType.Planks;
    public override string Name => "Planks";
    public override BlockBreakMaterial BreakMaterial => BlockBreakMaterial.Wooden;
    public override bool IsFlamable => true;
    public override float Hardness => 2.0f;

    public override TextureCoords TopTextureCoords => UvHelper.FromTileCoords(2, 2);
    public override TextureCoords BottomTextureCoords => TopTextureCoords;
    public override TextureCoords SideTextureCoords => TopTextureCoords;
}
