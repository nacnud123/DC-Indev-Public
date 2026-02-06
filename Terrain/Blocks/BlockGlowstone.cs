using VoxelEngine.Rendering;
using VoxelEngine.Utils;

namespace VoxelEngine.Terrain.Blocks;

public class BlockGlowstone : Block
{
    public override BlockType Type => BlockType.Glowstone;
    public override string Name => "Glowstone";
    public override int LightEmission => 15;
    public override float Hardness => 0.3f;
    
    public override TextureCoords TopTextureCoords => UvHelper.FromTileCoords(4, 3);
    public override TextureCoords BottomTextureCoords => TopTextureCoords;
    public override TextureCoords SideTextureCoords => TopTextureCoords;
}
