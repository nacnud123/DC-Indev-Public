using VoxelEngine.Rendering;
using VoxelEngine.Utils;

namespace VoxelEngine.Terrain.Blocks;

public class BlockGlass : Block
{
    public override BlockType Type => BlockType.Glass;
    public override string Name => "Glass";
    public override float Hardness => 0.2f;
    public override int LightOpacity => 2;

    public override TextureCoords TopTextureCoords => UvHelper.FromTileCoords(2, 1);
    public override TextureCoords BottomTextureCoords => TopTextureCoords;
    public override TextureCoords SideTextureCoords => TopTextureCoords;
}

