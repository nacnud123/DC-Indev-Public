using VoxelEngine.Rendering;
using VoxelEngine.Utils;

namespace VoxelEngine.Terrain.Blocks;

public class BlockLeaves : Block
{
    public override BlockType Type => BlockType.Leaves;
    public override string Name => "Leaves";
    public override BlockBreakMaterial BreakMaterial => BlockBreakMaterial.Grass;
    public override bool IsFlamable => true;
    public override float Hardness => 0.2f;
    public override int LightOpacity => 1;

    public override TextureCoords TopTextureCoords => UvHelper.FromTileCoords(1, 2);
    public override TextureCoords BottomTextureCoords => TopTextureCoords;
    public override TextureCoords SideTextureCoords => TopTextureCoords;
}
