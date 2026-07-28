using VoxelEngine.Rendering;
using VoxelEngine.Utils;

namespace VoxelEngine.Terrain.Blocks;

/// <summary>Absorbs nearby fluid blocks (water/lava) within ABSORB_RADIUS when placed or on a random
/// tick, turning them to air. When the sponge itself is removed, any fluids just outside its former
/// absorb range are re-scheduled so they resume flowing back in.</summary>
public class BlockSponge : Block
{
    // Cubic radius (in blocks) searched for fluids to soak up / to re-trigger on removal.
    public const int ABSORB_RADIUS = 3;

    public override BlockType Type => BlockType.Sponge;
    public override string Name => "Sponge";
    public override float Hardness => 0.5f;
    public override bool TicksRandomly => true; // Keeps re-absorbing any fluid that flows back in over time.
    public override BlockBreakMaterial BreakMaterial => BlockBreakMaterial.Dirt;

    public override TextureCoords TopTextureCoords => UvHelper.FromTileCoords(6, 1);
    public override TextureCoords BottomTextureCoords => TopTextureCoords;
    public override TextureCoords SideTextureCoords => TopTextureCoords;

    // Both placement and random ticks perform the same immediate fluid-soak-up sweep.
    public override void OnPlaced(World world, int x, int y, int z) => AbsorbFluids(world, x, y, z);
    public override void RandomTick(World world, int x, int y, int z, Random random) => AbsorbFluids(world, x, y, z);

    /// <summary>When the sponge is destroyed, fluids just beyond the absorb radius (which the sponge
    /// was holding back) need a tick scheduled so the flow simulation notices they can now spread
    /// back into the now-dry area. Sweeps one block further out than AbsorbFluids' radius.</summary>
    public override void OnRemoved(World world, int x, int y, int z)
    {
        for (int dx = -(ABSORB_RADIUS + 1) ; dx <= (ABSORB_RADIUS + 1); dx++)
        for (int dy = -(ABSORB_RADIUS + 1); dy <= (ABSORB_RADIUS + 1); dy++)
        for (int dz = -(ABSORB_RADIUS + 1); dz <= (ABSORB_RADIUS + 1); dz++)
        {
            if (BlockRegistry.IsFluid(world.GetBlock(x + dx, y + dy, z + dz)))
                world.ScheduleBlockTick(x + dx, y + dy, z + dz);
        }
    }

    // Scans the cube of blocks within ABSORB_RADIUS and clears out any fluid found, turning it to air.
    private static void AbsorbFluids(World world, int x, int y, int z)
    {
        for (int dx = -ABSORB_RADIUS; dx <= ABSORB_RADIUS; dx++)
        for (int dy = -ABSORB_RADIUS; dy <= ABSORB_RADIUS; dy++)
        for (int dz = -ABSORB_RADIUS; dz <= ABSORB_RADIUS; dz++)
        {
            if (BlockRegistry.IsFluid(world.GetBlock(x + dx, y + dy, z + dz)))
                world.SetBlock(x + dx, y + dy, z + dz, BlockType.Air);
        }
    }
}
