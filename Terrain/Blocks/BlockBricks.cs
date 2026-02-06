using VoxelEngine.Rendering;
using VoxelEngine.Utils;

namespace VoxelEngine.Terrain.Blocks;

public class BlockBricks : Block
{
    public override BlockType Type => BlockType.Bricks;
    public override string Name => "Bricks";

    public override TextureCoords TopTextureCoords => UvHelper.FromTileCoords(7, 0);
    public override TextureCoords BottomTextureCoords => TopTextureCoords;
    public override TextureCoords SideTextureCoords => TopTextureCoords;
}
