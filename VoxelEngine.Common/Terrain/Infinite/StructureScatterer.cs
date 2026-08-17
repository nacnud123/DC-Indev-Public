// Scatters the Resources/Structures prefabs through infinite terrain | DA

namespace VoxelEngine.Terrain.Infinite;

/// <summary>
/// Places the JSON prefabs under Resources/Structures/ into streamed chunks.
/// <para>
/// The old path stamped structures through <see cref="StructureLoader.PlaceRandomly"/> once, into a
/// 96-block box around spawn, on the frame a new world was created - which needed the whole area
/// resident, only worked in singleplayer, and left the rest of an endless world bare. This runs
/// inside chunk generation instead, so structures appear everywhere and the server produces the
/// same ones the client would.
/// </para>
/// <para>
/// Every decision is a pure function of (world seed, host chunk). A chunk asks each of its nine
/// neighbours "do you host a structure, and where?", then stamps whatever part of the answer falls
/// inside its own 16x16 footprint - so a structure straddling a chunk border comes out whole no
/// matter which side generates first, without either chunk touching the other's block array.
/// </para>
/// </summary>
public sealed class StructureScatterer
{
    private const int SEA_LEVEL = 64;

    // Largest prefab is 7x7, so a structure can only ever spill into a directly adjacent chunk.
    private const int HOST_RADIUS_CHUNKS = 1;

    // Rejects hillsides: a prefab is a rigid box, so on steep ground it either floats or buries.
    private const int MAX_GROUND_SLOPE = 3;

    private enum Placement { Surface, Underground }

    private sealed class Def
    {
        public required Structure Structure;
        public required Placement Placement;
        /// <summary>Probability that any given chunk hosts this one.</summary>
        public required double Chance;
        /// <summary>Blocks to sink the prefab's own origin below the surface.</summary>
        public int SinkY;
        /// <summary>Weathering pass: each matching block has RandomChance of becoming RandomTo.</summary>
        public BlockType RandomFrom, RandomTo;
        public double RandomChance;
    }

    private readonly int mSeed;
    private readonly Func<int, int, int> mHeightAt;
    private readonly List<Def> mDefs = new();
    private readonly Def? mSpawnHouse;

    /// <param name="heightAt">
    /// The generator's surface height for a world column. Must be pure noise - the host chunk is
    /// usually not resident when we ask, so there are no blocks to read a height off.
    /// </param>
    public StructureScatterer(int seed, Func<int, int, int> heightAt)
    {
        mSeed = seed;
        mHeightAt = heightAt;

        // Loaded once, up front: StructureLoader's cache is a plain Dictionary and generation runs
        // on several worker threads at once.
        //
        // The chances below are per chunk, and roughly half of them are rejected downstream - only
        // 42% of the world is above sea level, and steep ground is turned down as well - so the
        // rate you actually walk past is about half what is written here.
        var loader = new StructureLoader();

        mSpawnHouse = TryLoad(loader, "SpawnHouse.json", Placement.Surface, chance: 0, sinkY: 1);

        Add(TryLoad(loader, "tower.json", Placement.Surface, chance: 0.009));
        Add(TryLoad(loader, "pyramid.json", Placement.Surface, chance: 0.008));
        Add(TryLoad(loader, "obelisk.json", Placement.Surface, chance: 0.009, sinkY: 1));
        Add(TryLoad(loader, "fountain.json", Placement.Surface, chance: 0.008, sinkY: 2));

        var dungeon = TryLoad(loader, "dungeon.json", Placement.Underground, chance: 0.02);
        if (dungeon != null)
        {
            dungeon.RandomFrom = BlockType.CobbleStone;
            dungeon.RandomTo = BlockType.MossyCobblestone;
            dungeon.RandomChance = 0.5;
        }
        Add(dungeon);
    }

    private void Add(Def? def)
    {
        if (def != null)
            mDefs.Add(def);
    }

    // A missing or malformed prefab must not take the whole world down with it - the rest still
    // generate, and the file name is on the console.
    private static Def? TryLoad(StructureLoader loader, string file, Placement placement, double chance, int sinkY = 0)
    {
        try
        {
            return new Def
            {
                Structure = loader.Load(file),
                Placement = placement,
                Chance = chance,
                SinkY = sinkY,
            };
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[Structures] Could not load {file}: {ex.Message}");
            return null;
        }
    }

    /// <summary>
    /// Stamps every structure overlapping this chunk into it. Called on the generation worker after
    /// caves, ores and trees, so nothing carves a structure back out again.
    /// </summary>
    public void ScatterInto(Chunk chunk, int chunkX, int chunkZ)
    {
        for (int dx = -HOST_RADIUS_CHUNKS; dx <= HOST_RADIUS_CHUNKS; dx++)
        for (int dz = -HOST_RADIUS_CHUNKS; dz <= HOST_RADIUS_CHUNKS; dz++)
        {
            StampHost(chunk, chunkX, chunkZ, chunkX + dx, chunkZ + dz);
        }
    }

    private void StampHost(Chunk target, int targetX, int targetZ, int hostX, int hostZ)
    {
        // Same host chunk always yields the same draws in the same order, whichever neighbour is
        // asking - that identical sequence is what makes a cross-border structure line up.
        var rng = new Random(HostSeed(mSeed, hostX, hostZ));

        var def = Pick(hostX, hostZ, rng);
        if (def == null)
            return;

        // The spawn house is pinned rather than rolled, a few blocks off the origin column so the
        // spawn search still finds bare ground instead of putting the player on its roof.
        int originX = def == mSpawnHouse ? 2 : hostX * Chunk.WIDTH + rng.Next(Chunk.WIDTH);
        int originZ = def == mSpawnHouse ? 2 : hostZ * Chunk.DEPTH + rng.Next(Chunk.DEPTH);

        if (!TryFindOriginY(def, originX, originZ, rng, out int originY))
            return;

        Stamp(target, targetX, targetZ, def, originX, originY, originZ, rng);
    }

    // The spawn chunk always gets the house, so a fresh world still opens on something built. It
    // sits clear of the x=0,z=0 column the spawn search uses, or the player lands on its roof.
    private Def? Pick(int hostX, int hostZ, Random rng)
    {
        if (hostX == 0 && hostZ == 0 && mSpawnHouse != null)
            return mSpawnHouse;

        double roll = rng.NextDouble(), cumulative = 0;
        foreach (var def in mDefs)
        {
            cumulative += def.Chance;
            if (roll < cumulative)
                return def;
        }

        return null;
    }

    private bool TryFindOriginY(Def def, int originX, int originZ, Random rng, out int originY)
    {
        originY = 0;

        var s = def.Structure;

        // Underwater is out for both kinds: a surface prefab would drown, and a dungeon under the
        // seabed tends to open into the ocean. Exactly sea level is dry beach, so it stays.
        int ground = mHeightAt(originX, originZ);
        if (ground < SEA_LEVEL)
            return false;

        if (def.Placement == Placement.Underground)
        {
            int top = 40 - s.SizeY;
            if (top < 10)
                return false;

            originY = rng.Next(10, top + 1);
            return true;
        }

        int min = ground, max = ground;
        foreach (var (cx, cz) in new[] { (originX + s.SizeX - 1, originZ), (originX, originZ + s.SizeZ - 1), (originX + s.SizeX - 1, originZ + s.SizeZ - 1) })
        {
            int h = mHeightAt(cx, cz);
            min = Math.Min(min, h);
            max = Math.Max(max, h);
        }

        if (max - min > MAX_GROUND_SLOPE)
            return false;

        originY = min - def.SinkY;
        return originY >= 1 && originY + s.SizeY < Chunk.HEIGHT;
    }

    private static void Stamp(Chunk target, int targetX, int targetZ, Def def,
        int originX, int originY, int originZ, Random rng)
    {
        int baseX = targetX * Chunk.WIDTH, baseZ = targetZ * Chunk.DEPTH;

        foreach (var block in def.Structure.Blocks)
        {
            var type = block.Block;

            // Drawn before the clip test, not after: the weathering rolls have to advance in step
            // across every chunk this structure touches or the two halves come out mismatched.
            if (def.RandomChance > 0 && type == def.RandomFrom && rng.NextDouble() < def.RandomChance)
                type = def.RandomTo;

            int lx = originX + block.X - baseX;
            int lz = originZ + block.Z - baseZ;
            int y = originY + block.Y;

            if (lx < 0 || lx >= Chunk.WIDTH || lz < 0 || lz >= Chunk.DEPTH || y < 1 || y >= Chunk.HEIGHT)
                continue;

            // Air is placed, not skipped - it's what hollows out interiors and clears the ground a
            // prefab's box is meant to occupy.
            target.SetBlockGenerating(lx, y, lz, type);
        }
    }

    // Same reasoning as InfiniteTerrainGenerator.ChunkSeed: HashCode.Combine is randomized per
    // process, which would give a world different structures every time it loaded.
    private static int HostSeed(int seed, int chunkX, int chunkZ)
    {
        unchecked
        {
            int h = seed ^ 0x5F3A7C1D;
            h = h * 31 + chunkX * (int)0x9E3779B1;
            h = h * 31 + chunkZ * (int)0x85EBCA6B;
            h ^= h >> 15;
            h *= (int)0xC2B2AE35;
            h ^= h >> 13;
            return h;
        }
    }
}
