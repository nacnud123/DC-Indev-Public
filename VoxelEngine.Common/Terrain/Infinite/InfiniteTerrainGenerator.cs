namespace VoxelEngine.Terrain.Infinite;

public sealed class InfiniteTerrainGenerator
{
    private const int SEA_LEVEL = 64, BEDROCK_HEIGHT = 3, DIRT_DEPTH = 4;

    private readonly int mSeed;

    // FastNoiseLite is read-only once configured, so one instance is safe to share across chunks
    // generating concurrently on different threads.
    private readonly FastNoiseLite mContinent, mDetail;

    private readonly StructureScatterer mStructures;

    public InfiniteTerrainGenerator(int seed)
    {
        mSeed = seed;
        mContinent = MakeNoise(seed, octaves: 5, frequency: 0.004f);
        mDetail = MakeNoise(seed + 1, octaves: 4, frequency: 0.02f);

        // GetHeight is pure noise, which is what lets the scatterer decide where a structure sits
        // without the chunk under it existing yet.
        mStructures = new StructureScatterer(seed, GetHeight);
    }

    private static FastNoiseLite MakeNoise(int seed, int octaves, float frequency)
    {
        var fn = new FastNoiseLite(seed);
        fn.SetNoiseType(FastNoiseLite.NoiseType.Perlin);
        fn.SetFractalType(FastNoiseLite.FractalType.FBm);
        fn.SetFractalOctaves(octaves);
        fn.SetFrequency(frequency);
        return fn;
    }

    // NOT HashCode.Combine. randomized per process. This mix is stable forever, on every machine.
    private static int ChunkSeed(int seed, int chunkX, int chunkZ)
    {
        unchecked
        {
            int h = seed;
            h = h * 31 + chunkX * (int)0x9E3779B1; // Added explicit int cast
            h = h * 31 + chunkZ * (int)0x85EBCA6B; // Added explicit int cast
            h ^= h >> 15;
            h *= (int)0xC2B2AE35; // Added explicit int cast
            h ^= h >> 13;
            return h;
        }
    }


    private int GetHeight(int wx, int wz) =>
        SEA_LEVEL + (int)(mContinent.GetNoise(wx, wz) * 28.0 + mDetail.GetNoise(wx, wz) * 6.0);

    // The two-2D-noise cave stand-in that used to live here never carved a single block: its
    // threshold was 0.55, but the product of two FBm Perlin fields peaks around 0.35, so the test
    // was unreachable rather than merely rare. Caves now come from the original CaveCarver - worms,
    // branching tunnels, caverns and ravines - which was already per-chunk and seed-deterministic.

    public void Generate(Chunk chunk, int chunkX, int chunkZ)
    {
        int originX = chunkX * Chunk.WIDTH, originZ = chunkZ * Chunk.DEPTH;
        var rng = new Random(ChunkSeed(mSeed, chunkX, chunkZ)); // per-chunk, unshared: no locking

        for (int lx = 0; lx < Chunk.WIDTH; lx++)
        for (int lz = 0; lz < Chunk.DEPTH; lz++)
        {
            int wx = originX + lx, wz = originZ + lz;
            int height = GetHeight(wx, wz);

            for (int y = 0; y < Chunk.HEIGHT; y++)
            {
                BlockType block;
                if (y == 0) block = BlockType.Bedrock;
                else if (y < BEDROCK_HEIGHT && rng.NextDouble() < (BEDROCK_HEIGHT - y) / (double)BEDROCK_HEIGHT)
                    block = BlockType.Bedrock; // jagged, not a slab
                else if (y > height) block = y <= SEA_LEVEL ? BlockType.Water : BlockType.Air;
                else if (y == height) block = height <= SEA_LEVEL + 1 ? BlockType.Sand : BlockType.Grass;
                else if (y > height - DIRT_DEPTH) block = BlockType.Dirt;
                else block = BlockType.Stone;

                if (block != BlockType.Air) chunk.SetBlockGenerating(lx, y, lz, block);
            }
        }

        // Same phase order as the old TerrainGen: soil, then caves, then ores. Ores after caves so
        // veins aren't scattered into space the carver is about to hollow out.
        CaveCarver.CarveInto(chunk, mSeed);

        ScatterOres(chunk, rng);
        ScatterTrees(chunk, rng);

        // Last: a structure that had a cave or a tree land on top of it afterwards would come out
        // half-eaten, and the whole point of a prefab is that it arrives intact.
        mStructures.ScatterInto(chunk, chunkX, chunkZ);
    }

    private void ScatterOres(Chunk chunk, Random rng)
    {
        ScatterOreType(chunk, rng, BlockType.CoalOre, attempts: 6, minY: 5, maxY: 60, blobRadius: 2);
        ScatterOreType(chunk, rng, BlockType.IronOre, attempts: 4, minY: 5, maxY: 48, blobRadius: 2);
        ScatterOreType(chunk, rng, BlockType.GoldOre, attempts: 2, minY: 5, maxY: 28, blobRadius: 1);
        ScatterOreType(chunk, rng, BlockType.DiamondOre, attempts: 1, minY: 5, maxY: 14, blobRadius: 1);
    }

    private void ScatterOreType(Chunk chunk, Random rng, BlockType ore,
        int attempts, int minY, int maxY, int blobRadius)
    {
        for (int i = 0; i < attempts; i++)
        {
            int cx = rng.Next(Chunk.WIDTH), cy = rng.Next(minY, maxY + 1), cz = rng.Next(Chunk.DEPTH);

            for (int dx = -blobRadius; dx <= blobRadius; dx++)
            for (int dy = -blobRadius; dy <= blobRadius; dy++)
            for (int dz = -blobRadius; dz <= blobRadius; dz++)
            {
                if (dx * dx + dy * dy + dz * dz > blobRadius * blobRadius + 1) continue;

                int x = cx + dx, y = cy + dy, z = cz + dz;
                if (x < 0 || x >= Chunk.WIDTH || y < 0 || y >= Chunk.HEIGHT || z < 0 || z >= Chunk.DEPTH) continue;
                if (chunk.GetBlock(x, y, z) == BlockType.Stone) // never carve air or bedrock
                    chunk.SetBlockGenerating(x, y, z, ore);
            }
        }
    }

    // Kept inside this chunk's own footprint: neighbours may not exist yet, or may be generating on
    // another thread right now. A canopy that would cross a border is skipped - see Known gaps.
    private void ScatterTrees(Chunk chunk, Random rng)
    {
        const int TREES_PER_CHUNK = 2, TRUNK = 5, LEAF_R = 2;

        for (int i = 0; i < TREES_PER_CHUNK; i++)
        {
            int lx = rng.Next(LEAF_R + 1, Chunk.WIDTH - LEAF_R - 1);
            int lz = rng.Next(LEAF_R + 1, Chunk.DEPTH - LEAF_R - 1);

            int groundY = -1;
            for (int y = Chunk.HEIGHT - 1; y >= 0; y--)
                if (chunk.GetBlock(lx, y, lz) == BlockType.Grass)
                {
                    groundY = y;
                    break;
                }

            if (groundY < 0 || groundY + TRUNK + 2 >= Chunk.HEIGHT) continue;

            for (int ty = 1; ty <= TRUNK; ty++)
                chunk.SetBlockGenerating(lx, groundY + ty, lz, BlockType.Wood);

            for (int dx = -LEAF_R; dx <= LEAF_R; dx++)
            for (int dy = TRUNK - 2; dy <= TRUNK + 1; dy++)
            for (int dz = -LEAF_R; dz <= LEAF_R; dz++)
            {
                int x = lx + dx, y = groundY + dy, z = lz + dz;
                if (chunk.GetBlock(x, y, z) == BlockType.Air)
                    chunk.SetBlockGenerating(x, y, z, BlockType.Leaves);
            }
        }
    }
}