
using VoxelEngine.Core;
using VoxelEngine.Items;
using VoxelEngine.Rendering;
using VoxelEngine.Utils;

namespace VoxelEngine.Terrain.Blocks;

/// <summary>
/// Non-solid, damaging-by-nature (rendered via a dedicated Fire render type), self-consuming
/// and spreading block modeled closely on Minecraft Indev-era fire. Uses per-position metadata
/// as an age counter (0-15) that both gates when the fire attempts to spread and, combined with
/// neighboring fuel, determines when it burns out. Ages via ScheduledTick, which is invoked
/// every TickRate (20) ticks as long as the fire keeps rescheduling itself. Spread logic checks
/// two static flammability tables (GetEncouragement/GetCatchability) to decide which neighboring
/// blocks can ignite. Has no hardness (Hardness = 0) and drops nothing when removed.
/// </summary>
public class BlockFire : Block
{
    public override BlockType Type => BlockType.Fire;
    public override string Name => "Fire";
    public override RenderingType RenderType => RenderingType.Fire;
    public override BlockBreakMaterial BreakMaterial => BlockBreakMaterial.None;

    public override bool IsSolid => false;
    // Placing another block where fire currently is simply overwrites/extinguishes it.
    public override bool IsReplaceable => true;
    public override bool IsBreakable => true;
    // No mining time and no drop - fire is "broken" instantly and yields nothing.
    public override float Hardness => 0f;
    public override ItemStack? GetDrop(byte metadata) => null;
    // Never blocks light passing through it.
    public override int LightOpacity => 0;
    // Bright light source (near-max of the 0-15 scale), so fire lights up its surroundings.
    public override int LightEmission => 12;
    // Drives the ScheduledTick cadence below: fire re-evaluates itself every 20 ticks.
    public override int TickRate => 20;

    // Atlas tile (6,7); the actual animated flame look comes from RenderType.Fire in the
    // mesh builder/shader, not from switching texture coords here.
    public override TextureCoords TopTextureCoords => UvHelper.FromTileCoords(6, 7);
    public override TextureCoords BottomTextureCoords => TopTextureCoords;
    public override TextureCoords SideTextureCoords => TopTextureCoords;

    // --- Placement ---

    /// <summary>
    /// Called once immediately after fire is placed into the world (e.g. by TryIgniteNeighbor,
    /// flint-and-steel, lava, etc.). Verifies the fire has something to sustain it - either solid
    /// ground beneath it or at least one flammable neighbor - and immediately extinguishes itself
    /// (reverts to Air) if not. Otherwise schedules its first ScheduledTick.
    /// </summary>
    public override void OnPlaced(World world, int x, int y, int z)
    {
        // Fire needs a solid block below OR at least one flammable neighbor to survive.
        bool solidBelow = BlockRegistry.IsSolid(world.GetBlock(x, y - 1, z));
        if (!solidBelow && !CanNeighborCatchFire(world, x, y, z))
        {
            world.SetBlock(x, y, z, BlockType.Air);
            return;
        }
        world.ScheduleBlockTick(x, y, z);
    }

    // --- Scheduled tick (fires every 20 game ticks) ---

    /// <summary>
    /// Core fire simulation step, run every TickRate (20) ticks while the fire keeps
    /// rescheduling itself. Three phases per call: (1) age the fire via its metadata byte,
    /// (2) decide whether the fire survives this tick or burns out, (3) attempt to spread to
    /// nearby flammable blocks. See inline PHASE comments below for the exact rules.
    /// </summary>
    public override void ScheduledTick(World world, int x, int y, int z, Random random)
    {
        // PHASE 1: age the fire (metadata 0-15)
        int age = world.GetMetadata(x, y, z);
        if (age < 15)
        {
            age++;
            world.SetMetadata(x, y, z, (byte)age);
        }

        // PHASE 2: survival check
        bool hasFlammableNeighbor = CanNeighborCatchFire(world, x, y, z);
        bool solidBelow = BlockRegistry.IsSolid(world.GetBlock(x, y - 1, z));

        if (!hasFlammableNeighbor)
        {
            // Without fuel, fire briefly survives on solid ground (age 0-3), then dies.
            if (!solidBelow || age > 3)
            {
                world.SetBlock(x, y, z, BlockType.Air);
                return;
            }
        }
        else
        {
            // Fully-matured fire standing on non-flammable ground: 25% chance to self-extinguish.
            bool flammableBelow = GetEncouragement(world.GetBlock(x, y - 1, z)) > 0;
            if (!flammableBelow && age == 15 && random.Next(4) == 0)
            {
                world.SetBlock(x, y, z, BlockType.Air);
                return;
            }
        }

        // PHASE 3: spread (only at ages 10 and 15)
        if (age % 5 == 0 && age > 5)
        {
            // Mechanism A: directly ignite or consume the 6 face-adjacent blocks.
            // Y-1 has a much higher chance (100) so fire drops aggressively downward.
            TryIgniteNeighbor(world, x - 1, y,     z,     300, random);
            TryIgniteNeighbor(world, x + 1, y,     z,     300, random);
            TryIgniteNeighbor(world, x,     y - 1, z,     100, random);
            TryIgniteNeighbor(world, x,     y + 1, z,     200, random);
            TryIgniteNeighbor(world, x,     y,     z - 1, 300, random);
            TryIgniteNeighbor(world, x,     y,     z + 1, 300, random);

            // Mechanism B: long-range scan — any air block within a 3×3×6 volume
            // (3 wide, 6 tall extending above) can spontaneously ignite if a flammable
            // block neighbors it.  Height penalty makes upward leap increasingly rare.
            for (int nx = x - 1; nx <= x + 1; nx++)
            for (int ny = y - 1; ny <= y + 4; ny++)
            for (int nz = z - 1; nz <= z + 1; nz++)
            {
                if (nx == x && ny == y && nz == z) continue;
                if (world.GetBlock(nx, ny, nz) != BlockType.Air) continue;

                int encouragement = GetMaxEncouragement(world, nx, ny, nz);
                if (encouragement <= 0) continue;

                // Blocks above y+1 become progressively harder to ignite.
                int baseChance = ny > y + 1 ? 100 + (ny - (y + 1)) * 100 : 100;
                if (random.Next(baseChance) < encouragement)
                    world.SetBlock(nx, ny, nz, BlockType.Fire);
            }
        }

        // Reschedule for the next tick.  ScheduleBlockTick is a no-op if the fire
        // block was replaced during spread (e.g. by water), keeping the queue clean.
        world.ScheduleBlockTick(x, y, z);
    }

    // --- Visual (cosmetic only, no gameplay effect) ---

    /// <summary>Purely cosmetic per-frame smoke particle spawn; does not affect simulation state.</summary>
    public override void RandomDisplayTick(int x, int y, int z, Random random)
    {
        GameContext.Current?.ParticleSystem?.SpawnSmokeParticle(new Vector3(x, y, z));
    }

    // --- Static flammability tables (Indev values) ---
    //
    // GetEncouragement  how strongly a block fuels nearby fire (higher = fire lasts longer)
    // GetCatchability   how easily a block ignites (higher = catches fire faster)

    /// <summary>
    /// "Encouragement" value for a block type: how strongly it sustains fire that is adjacent
    /// to it. Used both to decide whether fire should keep burning (survival check) and, in
    /// GetMaxEncouragement, to weight ignition chance during spread. 0 means the block gives
    /// fire no fuel at all (non-flammable).
    /// </summary>
    public static int GetEncouragement(BlockType type) => type switch
    {
        BlockType.Planks        => 5,
        BlockType.Wood          => 5,
        BlockType.WoodSlab      => 5,
        BlockType.DoubleWoodSlab => 5,
        BlockType.WoodenStair   => 5,
        BlockType.Leaves        => 30,
        BlockType.Bookcase      => 30,
        BlockType.TNT           => 15,
        BlockType.Black         => 30,
        BlockType.Blue          => 30,
        BlockType.Green         => 30,
        BlockType.Red           => 30,
        BlockType.White         => 30,
        BlockType.YellowFlower  => 30,
        BlockType.RedFlower     => 30,
        BlockType.BrownMushroom => 30,
        BlockType.RedMushroom   => 30,
        BlockType.GrassTuft     => 30,
        BlockType.Sapling       => 30,
        BlockType.DuncanBlock   => 5,
        _ => 0
    };

    /// <summary>
    /// "Catchability" value for a block type: how likely it is to actually ignite (or be
    /// consumed) when TryIgniteNeighbor rolls against it. Higher = catches faster/more often.
    /// 0 means the block cannot catch fire at all.
    /// </summary>
    public static int GetCatchability(BlockType type) => type switch
    {
        BlockType.Planks        => 20,
        BlockType.Wood          => 5,
        BlockType.WoodSlab      => 20,
        BlockType.DoubleWoodSlab => 20,
        BlockType.WoodenStair   => 20,
        BlockType.Leaves        => 60,
        BlockType.Bookcase      => 20,
        BlockType.TNT           => 100,
        BlockType.Black         => 60,
        BlockType.Blue          => 60,
        BlockType.Green         => 60,
        BlockType.Red           => 60,
        BlockType.White         => 60,
        BlockType.YellowFlower  => 60,
        BlockType.RedFlower     => 60,
        BlockType.BrownMushroom => 60,
        BlockType.RedMushroom   => 60,
        BlockType.GrassTuft     => 60,
        BlockType.Sapling       => 60,
        BlockType.DuncanBlock   => 20,
        _ => 0
    };

    /// <summary>Returns true if any of the 6 face-adjacent blocks can catch fire (encouragement > 0).</summary>
    public static bool CanNeighborCatchFire(World world, int x, int y, int z) =>
        GetEncouragement(world.GetBlock(x - 1, y, z)) > 0 ||
        GetEncouragement(world.GetBlock(x + 1, y, z)) > 0 ||
        GetEncouragement(world.GetBlock(x, y - 1, z)) > 0 ||
        GetEncouragement(world.GetBlock(x, y + 1, z)) > 0 ||
        GetEncouragement(world.GetBlock(x, y, z - 1)) > 0 ||
        GetEncouragement(world.GetBlock(x, y, z + 1)) > 0;

    // Returns the highest encouragement value among the 6 face-adjacent blocks.
    private static int GetMaxEncouragement(World world, int x, int y, int z)
    {
        int max = 0;
        max = Math.Max(max, GetEncouragement(world.GetBlock(x - 1, y, z)));
        max = Math.Max(max, GetEncouragement(world.GetBlock(x + 1, y, z)));
        max = Math.Max(max, GetEncouragement(world.GetBlock(x, y - 1, z)));
        max = Math.Max(max, GetEncouragement(world.GetBlock(x, y + 1, z)));
        max = Math.Max(max, GetEncouragement(world.GetBlock(x, y, z - 1)));
        max = Math.Max(max, GetEncouragement(world.GetBlock(x, y, z + 1)));
        return max;
    }

    // Attempt to ignite or consume the block at (x,y,z).
    // chance is the roll denominator — lower = more likely (Y-1 uses 100, sides use 300).
    private static void TryIgniteNeighbor(World world, int x, int y, int z, int chance, Random random)
    {
        var blockType = world.GetBlock(x, y, z);
        int catchability = GetCatchability(blockType);
        if (catchability <= 0) return;

        if (random.Next(chance) >= catchability) return;

        if (blockType == BlockType.TNT)
        {
            // Setting TNT to Air calls OnRemoved, which spawns the TntEntity fuse.
            world.SetBlock(x, y, z, BlockType.Air);
        }
        else if (random.Next(2) == 0)
        {
            world.SetBlock(x, y, z, BlockType.Fire);   // block catches fire
        }
        else
        {
            world.SetBlock(x, y, z, BlockType.Air);    // block is consumed/destroyed
        }
    }
}
