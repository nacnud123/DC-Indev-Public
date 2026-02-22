using VoxelEngine.Rendering;
using VoxelEngine.Utils;

namespace VoxelEngine.Terrain.Blocks;

public class BlockGrass : Block
{
    public override BlockType Type => BlockType.Grass;
    public override string Name => "Grass";
    public override float Hardness => 0.6f;
    public override BlockBreakMaterial BreakMaterial => BlockBreakMaterial.Grass;
    public override bool TicksRandomly => true;

    public override TextureCoords TopTextureCoords => UvHelper.FromTileCoords(0, 2);
    public override TextureCoords BottomTextureCoords => UvHelper.FromTileCoords(1, 1);
    public override TextureCoords SideTextureCoords => UvHelper.FromTileCoords(0, 1);
    public override TextureCoords InventoryTextureCoords => SideTextureCoords;

    public override void RandomTick(World world, int x, int y, int z, Random random)
    {
        int skyAbove = world.GetSkyLight(x, y + 1, z);
        var above = world.GetBlock(x, y + 1, z);

        // Death: low light + solid block above → 1-in-4 chance to become dirt
        if (skyAbove < 4 && BlockRegistry.IsSolid(above))
        {
            if (random.Next(4) == 0)
            {
                world.SetBlock(x, y, z, BlockType.Dirt);
                return;
            }
        }

        // Spread: sufficient light above → try converting a nearby dirt block
        if (skyAbove >= 9)
        {
            int tx = x + random.Next(-1, 2);
            int ty = y + random.Next(-3, 3);
            int tz = z + random.Next(-1, 2);

            if (world.GetBlock(tx, ty, tz) == BlockType.Dirt)
            {
                int tSkyAbove = world.GetSkyLight(tx, ty + 1, tz);
                var tAbove = world.GetBlock(tx, ty + 1, tz);
                if (tSkyAbove >= 4 && !BlockRegistry.IsSolid(tAbove))
                    world.SetBlock(tx, ty, tz, BlockType.Grass);
            }
        }
    }
}
