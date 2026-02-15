using VoxelEngine.Rendering;
using VoxelEngine.Utils;

namespace VoxelEngine.Terrain.Blocks;

public class BlockBlack : Block
{
    public override BlockType Type => BlockType.Black;
    public override string Name => "Black";
    public override BlockBreakMaterial BreakMaterial => BlockBreakMaterial.Wool;

    public override TextureCoords TopTextureCoords => UvHelper.FromTileCoords(3, 4);
    public override TextureCoords BottomTextureCoords => TopTextureCoords;
    public override TextureCoords SideTextureCoords => TopTextureCoords;
}
