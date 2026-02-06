using VoxelEngine.Rendering;
using VoxelEngine.Utils;

namespace VoxelEngine.Terrain.Blocks;

public class BlockBrownMushroom : Block
{
    public override BlockType Type => BlockType.BrownMushroom;
    public override string Name => "Brown Mushroom";
    public override RenderingType RenderType => RenderingType.Cross;
    public override bool IsSolid => false;
    public override int LightOpacity => 0;
    public override bool SuffocatesBeneath => true;

    public override TextureCoords TopTextureCoords => UvHelper.FromTileCoords(4, 2);
    public override TextureCoords BottomTextureCoords => TopTextureCoords;
    public override TextureCoords SideTextureCoords => TopTextureCoords;
}
