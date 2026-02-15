using VoxelEngine.Rendering;
using VoxelEngine.Utils;

namespace VoxelEngine.Terrain.Blocks;

public class BlockSpiderweb : Block
{
    public override BlockType Type => BlockType.SpiderWeb;
    public override string Name => "Spiderweb";
    public override RenderingType RenderType => RenderingType.Cross;
    public override BlockBreakMaterial BreakMaterial => BlockBreakMaterial.Wool;
    public override bool IsSolid => false;
    public override int LightOpacity => 0;
    public override float Hardness => 0.2f;
    public override bool SlowsEntities => true;

    public override TextureCoords TopTextureCoords => UvHelper.FromTileCoords(5, 3);
    public override TextureCoords BottomTextureCoords => TopTextureCoords;
    public override TextureCoords SideTextureCoords => TopTextureCoords;
}
