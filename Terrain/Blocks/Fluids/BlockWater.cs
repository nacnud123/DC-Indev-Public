using OpenTK.Mathematics;
using VoxelEngine.Core;
using VoxelEngine.Rendering;
using VoxelEngine.Utils;

namespace VoxelEngine.Terrain.Blocks;

public class BlockWater : Block
{
    public override BlockType Type => BlockType.Water;
    public override string Name => "Water";

    public override bool IsSolid => false;
    public override bool IsBreakable => false;
    public override bool IsTransparent => true;
    public override int LightOpacity => 3;
    public override bool ShowInInventory => true;
    public override bool TicksRandomly => true;

    public override TextureCoords TopTextureCoords => UvHelper.FromTileCoords(0, 4);
    public override TextureCoords BottomTextureCoords => TopTextureCoords;
    public override TextureCoords SideTextureCoords => TopTextureCoords;

    public override void RandomTick(World world, int x, int y, int z, Random random)
    {
        // Classic-style flood fill: down first, then horizontal
        TrySpread(world, x, y - 1, z);
        TrySpread(world, x + 1, y, z);
        TrySpread(world, x - 1, y, z);
        TrySpread(world, x, y, z + 1);
        TrySpread(world, x, y, z - 1);
    }

    private static void TrySpread(World world, int x, int y, int z)
    {
        var neighbor = world.GetBlock(x, y, z);
        if (neighbor == BlockType.Water || BlockRegistry.IsSolid(neighbor))
            return;

        if (IsNearSponge(world, x, y, z))
            return;

        // Water washes away non-solid blocks (torches, flowers, etc.)
        if (neighbor != BlockType.Air)
            Game.Instance?.ParticleSystem?.SpawnBlockBreakParticles(new Vector3(x, y, z), neighbor);

        world.SetBlock(x, y, z, BlockType.Water);
    }

    private static bool IsNearSponge(World world, int x, int y, int z)
    {
        int r = BlockSponge.ABSORB_RADIUS;
        for (int dx = -r; dx <= r; dx++)
        for (int dy = -r; dy <= r; dy++)
        for (int dz = -r; dz <= r; dz++)
        {
            if (world.GetBlock(x + dx, y + dy, z + dz) == BlockType.Sponge)
                return true;
        }
        return false;
    }
}
