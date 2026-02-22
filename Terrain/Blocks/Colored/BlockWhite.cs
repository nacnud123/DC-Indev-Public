using VoxelEngine.Rendering;
using VoxelEngine.Utils;

namespace VoxelEngine.Terrain.Blocks;

public class BlockWhite : Block
{
    public override BlockType Type => BlockType.White;
    public override string Name => "White";
    public override BlockBreakMaterial BreakMaterial => BlockBreakMaterial.Wool;
    public override bool IsFlamable => true;

    public override TextureCoords TopTextureCoords => UvHelper.FromTileCoords(3, 0);
    public override TextureCoords BottomTextureCoords => TopTextureCoords;
    public override TextureCoords SideTextureCoords => TopTextureCoords;
}
