using VoxelEngine.Rendering;
using VoxelEngine.Utils;

namespace VoxelEngine.Terrain.Blocks;

public class BlockGravel : Block
{
    public override BlockType Type => BlockType.Gravel;
    public override string Name => "Gravel";

    public override bool GravityBlock => true;

    public override TextureCoords TopTextureCoords => UvHelper.FromTileCoords(6, 2);
    public override TextureCoords BottomTextureCoords => TopTextureCoords;
    public override TextureCoords SideTextureCoords => TopTextureCoords;
}
