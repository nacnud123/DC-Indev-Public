
using VoxelEngine.Core;
using VoxelEngine.Rendering;
using VoxelEngine.Utils;

namespace VoxelEngine.Terrain.Blocks;

/// <summary>
/// Water fluid block. Like lava, every water block acts as a full source (no decaying
/// "fluid level" metadata is tracked here) but water flows much faster
/// (<see cref="TickRate"/> = 5) and, unlike lava, fills an entire open column below it
/// in one tick rather than descending a single block at a time. Emits no light but
/// blocks some light (LightOpacity 3) and is flagged transparent for rendering/culling.
/// Meeting lava converts the lava-adjacent block to cobblestone.
/// </summary>
public class BlockWater : Block
{
    public override BlockType Type => BlockType.Water;
    public override string Name => "Water";

    public override bool IsSolid => false;
    public override bool IsBreakable => false;
    // Marks this as a fluid for the engine's flow/physics handling (buoyancy, swimming, etc).
    public override bool IsFluid => true;
    public override bool IsTransparent => true;
    public override int LightOpacity => 3;
    public override bool ShowInInventory => true;
    // Fast tick rate relative to lava's 25 - water flows/spreads much more quickly.
    public override int TickRate => 5;

    public override TextureCoords TopTextureCoords => UvHelper.FromTileCoords(0, 4);
    public override TextureCoords BottomTextureCoords => TopTextureCoords;
    public override TextureCoords SideTextureCoords => TopTextureCoords;

    /// <summary>
    /// Drives water's flow each scheduled tick. First fills the entire open column
    /// directly below the source (descending until hitting water/solid/a sponge, unlike
    /// lava which only falls one block per tick), then spreads sideways into up to 4
    /// neighbors in a randomized order regardless of whether the vertical fill happened.
    /// </summary>
    public override void ScheduledTick(World world, int x, int y, int z, Random random)
    {
        // Flow down: fill the entire column below until blocked
        for (int ny = y - 1; ny >= 0; ny--)
        {
            var below = world.GetBlock(x, ny, z);

            if (below == BlockType.Water || BlockRegistry.IsSolid(below) || IsNearSponge(world, x, ny, z))
                break;

            if (below == BlockType.Lava)
            {
                Game.Instance.AudioManager.PlayAudio("Resources/Audio/SteamHiss.ogg", Game.Instance.AudioManager.SfxVol);
                world.SetBlock(x, ny, z, BlockType.CobbleStone);
                break;
            }

            if (below != BlockType.Air)
                Game.Instance?.ParticleSystem?.SpawnBlockBreakParticles(new Vector3(x, ny, z), below);

            world.SetBlock(x, ny, z, BlockType.Water);
        }

        // Horizontal spread: shuffled directions (Fisher-Yates), so spread order isn't
        // biased toward a fixed +x/-x/+z/-z ordering.
        var dirs = new (int dx, int dz)[] { (1, 0), (-1, 0), (0, 1), (0, -1) };
        for (int i = dirs.Length - 1; i > 0; i--)
        {
            int j = random.Next(i + 1);
            (dirs[i], dirs[j]) = (dirs[j], dirs[i]);
        }
        foreach (var (dx, dz) in dirs)
            TrySpread(world, x + dx, y, z + dz);
    }

    /// <summary>
    /// Attempts to convert a single neighboring block into water. Bails out if the
    /// neighbor is already water, solid, or within a sponge's absorb radius; converts to
    /// cobblestone (with a hiss sound) if the neighbor is lava, otherwise overwrites the
    /// neighbor with water (washing away whatever was there, e.g. torches/flowers, with
    /// a break-particle burst).
    /// </summary>
    private void TrySpread(World world, int x, int y, int z)
    {
        var neighbor = world.GetBlock(x, y, z);
        if (neighbor == BlockType.Water || BlockRegistry.IsSolid(neighbor))
            return;

        // Water + lava = cobblestone
        if (neighbor == BlockType.Lava)
        {
            Game.Instance.AudioManager.PlayAudio("Resources/Audio/SteamHiss.ogg", Game.Instance.AudioManager.SfxVol);
            world.SetBlock(x, y, z, BlockType.CobbleStone);
            return;
        }

        if (IsNearSponge(world, x, y, z))
            return;

        // Water washes away non-solid blocks (torches, flowers, etc.)
        if (neighbor != BlockType.Air)
            Game.Instance?.ParticleSystem?.SpawnBlockBreakParticles(new Vector3(x, y, z), neighbor);

        world.SetBlock(x, y, z, BlockType.Water);
    }

    /// <summary>
    /// Scans a cube of radius <see cref="BlockSponge.ABSORB_RADIUS"/> centered on the
    /// given position for a sponge block. Sponges suppress fluid spread/flow within
    /// their absorb radius, so water (and lava) flow checks call this before converting
    /// a neighboring block.
    /// </summary>
    private bool IsNearSponge(World world, int x, int y, int z)
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
