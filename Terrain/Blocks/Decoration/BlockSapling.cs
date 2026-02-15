using VoxelEngine.Rendering;
using VoxelEngine.Utils;

namespace VoxelEngine.Terrain.Blocks;

public class BlockSapling : Block
{
    public override BlockType Type => BlockType.Sapling;
    public override string Name => "Sapling";
    public override RenderingType RenderType => RenderingType.Cross;
    public override BlockBreakMaterial BreakMaterial => BlockBreakMaterial.Dirt;
    public override bool IsSolid => false;
    public override int LightOpacity => 0;
    public override float Hardness => 0.0f;
    public override bool TicksRandomly => true;
    public override bool SuffocatesBeneath => true;
    public override bool NeedsSupportBelow => true;
    public override List<BlockType> BlocksThatCanSupport => new List<BlockType>() { BlockType.Grass, BlockType.Dirt };

    public override TextureCoords TopTextureCoords => UvHelper.FromTileCoords(5, 4);
    public override TextureCoords BottomTextureCoords => TopTextureCoords;
    public override TextureCoords SideTextureCoords => TopTextureCoords;

    public override void RandomTick(World world, int x, int y, int z, Random random)
    {
        if (world.GetBlock(x, y + 1, z) != BlockType.Air)
            return;

        if (random.Next(5) != 0)
            return;

        world.GrowTree(x, y, z);
    }
}