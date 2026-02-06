using VoxelEngine.Rendering;
using VoxelEngine.Utils;

namespace VoxelEngine.Terrain.Blocks;

public class BlockSponge : Block
{
    public override BlockType Type => BlockType.Sponge;
    public override string Name => "Sponge";

    public override TextureCoords TopTextureCoords => UvHelper.FromTileCoords(6, 1);
    public override TextureCoords BottomTextureCoords => TopTextureCoords;
    public override TextureCoords SideTextureCoords => TopTextureCoords;
}
