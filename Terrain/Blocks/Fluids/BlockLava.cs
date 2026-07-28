
using VoxelEngine.Core;
using VoxelEngine.Rendering;
using VoxelEngine.Utils;

namespace VoxelEngine.Terrain.Blocks;

/// <summary>
/// Lava fluid block. Unlike water, lava does not use a decaying metadata "fluid level" -
/// every lava block behaves as a full source block that can flow downward or spread
/// sideways one block per scheduled tick, is much slower to update than water
/// (<see cref="TickRate"/> = 25 vs water's 5), emits maximum light, and turns into
/// cobblestone (with a hiss sound) wherever it meets water.
/// </summary>
public class BlockLava : Block
{
    public override BlockType Type => BlockType.Lava;
    public override string Name => "Lava";

    public override bool IsSolid => false;
    public override bool IsBreakable => false;
    // Marks this as a fluid for the engine's flow/physics handling (buoyancy, swimming, etc).
    public override bool IsFluid => true;
    public override bool IsTransparent => true;
    public override int LightEmission => 15;
    public override int LightOpacity => 3;
    public override bool ShowInInventory => true;
    // Scheduled ticks fire much less often than water's (25 vs 5), making lava flow
    // noticeably slower/thicker than water, matching classic lava behavior.
    public override int TickRate => 25;

    public override TextureCoords TopTextureCoords => UvHelper.FromTileCoords(7, 4);
    public override TextureCoords BottomTextureCoords => TopTextureCoords;
    public override TextureCoords SideTextureCoords => TopTextureCoords;

    /// <summary>
    /// Drives lava's flow each scheduled tick. First tries to fall one block straight
    /// down (lava only ever descends a single block per tick, not an entire column like
    /// water); if it can't fall, it instead spreads sideways into up to 4 neighbors in a
    /// randomized order so flow direction isn't visually biased. Falling and spreading
    /// are mutually exclusive per tick - downward flow always takes priority.
    /// </summary>
    public override void ScheduledTick(World world, int x, int y, int z, Random random)
    {
        // Try to flow down one block (lava descends one step at a time)
        bool flowedDown = false;
        var below = world.GetBlock(x, y - 1, z);
        if (below != BlockType.Lava && !BlockRegistry.IsSolid(below) && !IsNearSponge(world, x, y - 1, z))
        {
            if (below == BlockType.Water)
            {
                Game.Instance.AudioManager.PlayAudio("Resources/Audio/SteamHiss.ogg", Game.Instance.AudioManager.SfxVol);
                world.SetBlock(x, y - 1, z, BlockType.CobbleStone);
            }
            else
            {
                if (below != BlockType.Air)
                    Game.Instance?.ParticleSystem?.SpawnBlockBreakParticles(new Vector3(x, y - 1, z), below);

                world.SetBlock(x, y - 1, z, BlockType.Lava);
                flowedDown = true;
            }
        }

        // Only spread horizontally if downward flow did not occur
        if (!flowedDown)
        {
            // Fisher-Yates shuffle of the 4 cardinal directions so spread order (and
            // therefore visual flow direction when multiple sides are open) is randomized
            // rather than always favoring +x/-x/+z/-z in a fixed order.
            var dirs = new (int dx, int dz)[] { (1, 0), (-1, 0), (0, 1), (0, -1) };
            for (int i = dirs.Length - 1; i > 0; i--)
            {
                int j = random.Next(i + 1);
                (dirs[i], dirs[j]) = (dirs[j], dirs[i]);
            }
            foreach (var (dx, dz) in dirs)
                TrySpread(world, x + dx, y, z + dz);
        }
    }

    /// <summary>
    /// Attempts to convert a single neighboring block into lava. Bails out if the
    /// neighbor is already lava, solid, or within a sponge's absorb radius; converts to
    /// cobblestone (with a hiss sound) if the neighbor is water, otherwise overwrites
    /// the neighbor with lava (destroying whatever was there, e.g. torches/flowers,
    /// with a break-particle burst).
    /// </summary>
    private void TrySpread(World world, int x, int y, int z)
    {
        var neighbor = world.GetBlock(x, y, z);

        if (neighbor == BlockType.Lava || BlockRegistry.IsSolid(neighbor))
            return;

        if (neighbor == BlockType.Water)
        {
            Game.Instance.AudioManager.PlayAudio("Resources/Audio/SteamHiss.ogg", Game.Instance.AudioManager.SfxVol);
            world.SetBlock(x, y, z, BlockType.CobbleStone);
            return;
        }

        if (IsNearSponge(world, x, y, z))
            return;

        if (neighbor != BlockType.Air)
            Game.Instance?.ParticleSystem?.SpawnBlockBreakParticles(new Vector3(x, y, z), neighbor);

        world.SetBlock(x, y, z, BlockType.Lava);
    }

    /// <summary>
    /// Scans a cube of radius <see cref="BlockSponge.ABSORB_RADIUS"/> centered on the
    /// given position for a sponge block. Sponges suppress fluid spread/flow within
    /// their absorb radius, so lava (and water) flow checks call this before converting
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
