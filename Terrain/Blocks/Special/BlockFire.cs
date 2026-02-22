using OpenTK.Mathematics;
using VoxelEngine.Core;
using VoxelEngine.Rendering;
using VoxelEngine.Utils;

namespace VoxelEngine.Terrain.Blocks;

public class BlockFire : Block
{
    public override BlockType Type => BlockType.Fire;
    public override string Name => "Fire";
    public override RenderingType RenderType => RenderingType.Fire;
    public override BlockBreakMaterial BreakMaterial => BlockBreakMaterial.None;

    public override bool IsSolid => false;
    public override bool IsReplaceable => true;
    public override bool IsBreakable => true;
    public override float Hardness => 0f;
    public override int LightOpacity => 0;
    public override int LightEmission => 12;
    public override int TickRate => 20;

    public override TextureCoords TopTextureCoords => UvHelper.FromTileCoords(6, 7);
    public override TextureCoords BottomTextureCoords => TopTextureCoords;
    public override TextureCoords SideTextureCoords => TopTextureCoords;

    // --- Placement ---

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

    public override void RandomDisplayTick(int x, int y, int z, Random random)
    {
        Game.Instance?.ParticleSystem?.SpawnSmokeParticle(new Vector3(x, y, z));
    }

    // --- Static flammability tables (Indev values) ---
    //
    // GetEncouragement  how strongly a block fuels nearby fire (higher = fire lasts longer)
    // GetCatchability   how easily a block ignites (higher = catches fire faster)

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

    // Returns true if any of the 6 face-adjacent blocks can catch fire.
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
