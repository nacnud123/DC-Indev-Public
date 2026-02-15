using VoxelEngine.Rendering;
using VoxelEngine.Utils;

namespace VoxelEngine.Terrain.Blocks;

public class BlockSand : Block
{
    public override BlockType Type => BlockType.Sand;
    public override string Name => "Sand";
    public override BlockBreakMaterial BreakMaterial => BlockBreakMaterial.Sand;

    public override bool GravityBlock => true;

    public override float Hardness => 0.5f;
    
    public override TextureCoords TopTextureCoords => UvHelper.FromTileCoords(0, 3);
    public override TextureCoords BottomTextureCoords => TopTextureCoords;
    public override TextureCoords SideTextureCoords => TopTextureCoords;
}
