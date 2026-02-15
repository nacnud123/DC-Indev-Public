using VoxelEngine.Rendering;
using VoxelEngine.Utils;

namespace VoxelEngine.Terrain.Blocks;

public class BlockDirt : Block
{
    public override BlockType Type => BlockType.Dirt;
    public override string Name => "Dirt";
    public override float Hardness => 0.5f;
    public override bool TicksRandomly => true;
    public override BlockBreakMaterial BreakMaterial => BlockBreakMaterial.Dirt;

    public override TextureCoords TopTextureCoords => UvHelper.FromTileCoords(1, 1);
    public override TextureCoords BottomTextureCoords => TopTextureCoords;
    public override TextureCoords SideTextureCoords => TopTextureCoords;

    public override void RandomTick(World world, int x, int y, int z, Random random)
    {
        if (world.GetBlock(x, y + 1, z) != BlockType.Air)
            return;

        if (world.GetBlock(x + 1, y, z) == BlockType.Grass ||
            world.GetBlock(x - 1, y, z) == BlockType.Grass ||
            world.GetBlock(x, y, z + 1) == BlockType.Grass ||
            world.GetBlock(x, y, z - 1) == BlockType.Grass ||
            world.GetBlock(x, y - 1, z) == BlockType.Grass)
        {
            world.SetBlock(x, y, z, BlockType.Grass);
        }
    }
}
