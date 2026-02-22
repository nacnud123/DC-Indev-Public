using VoxelEngine.Rendering;
using VoxelEngine.Utils;

namespace VoxelEngine.Terrain.Blocks;

public class BlockRedFlower : Block
{
    public override BlockType Type => BlockType.RedFlower;
    public override string Name => "Red Flower";
    public override RenderingType RenderType => RenderingType.Cross;
    public override BlockBreakMaterial BreakMaterial => BlockBreakMaterial.Grass;
    public override bool IsFlamable => true;
    public override bool IsSolid => false;
    public override bool IsReplaceable => true;
    public override int LightOpacity => 0;
    public override float Hardness => 0.0f;
    public override bool SuffocatesBeneath => true;
    public override bool NeedsSupportBelow => true;
    public override List<BlockType> BlocksThatCanSupport => new List<BlockType>() { BlockType.Grass, BlockType.Dirt};

    public override TextureCoords TopTextureCoords => UvHelper.FromTileCoords(4, 3);
    public override TextureCoords BottomTextureCoords => TopTextureCoords;
    public override TextureCoords SideTextureCoords => TopTextureCoords;
}
