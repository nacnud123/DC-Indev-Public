using VoxelEngine.Rendering;
using VoxelEngine.Utils;

namespace VoxelEngine.Terrain.Blocks;

public class BlockWood : Block
{
    public override BlockType Type => BlockType.Wood;
    public override string Name => "Wood";
    public override BlockBreakMaterial BreakMaterial => BlockBreakMaterial.Wooden;

    public override float Hardness => 2.0f;
    
    public override TextureCoords TopTextureCoords => UvHelper.FromTileCoords(2, 0);
    public override TextureCoords BottomTextureCoords => TopTextureCoords;
    public override TextureCoords SideTextureCoords => UvHelper.FromTileCoords(1, 0);
}
