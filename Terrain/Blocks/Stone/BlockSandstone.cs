using VoxelEngine.Rendering;
using VoxelEngine.Utils;

namespace VoxelEngine.Terrain.Blocks;

public class BlockSandstone: Block
{
    public override BlockType Type => BlockType.Sandstone;
    public override string Name => "Sandstone";
    public override BlockBreakMaterial BreakMaterial => BlockBreakMaterial.Stone;

    public override float Hardness => 1.5f;
    
    public override TextureCoords TopTextureCoords => UvHelper.FromTileCoords(6,4);
    public override TextureCoords BottomTextureCoords => TopTextureCoords;
    public override TextureCoords SideTextureCoords => TopTextureCoords;
}
