using VoxelEngine.Rendering;
using VoxelEngine.Utils;

namespace VoxelEngine.Terrain.Blocks;
public class BlockClay : Block
{
    public override BlockType Type => BlockType.Clay;
    public override string Name => "Clay";
    public override BlockBreakMaterial BreakMaterial => BlockBreakMaterial.Sand;

    public override float Hardness => .2f;

    public override TextureCoords TopTextureCoords => UvHelper.FromTileCoords(6, 3);
    public override TextureCoords BottomTextureCoords => TopTextureCoords;
    public override TextureCoords SideTextureCoords => TopTextureCoords;
}
