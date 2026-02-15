// Main terrain generation file, has functions related to cave, world, and ore generation. Also, has a lot of consts | DA | 2/14/26
// Uses Simplex Noise. Uses a standard permutation table (256 entries, doubled to 512 to avoid modulo) and gradient functions that dot-product with simplex vertices.
using VoxelEngine.Core;
using VoxelEngine.Terrain.Blocks;

namespace VoxelEngine.Terrain;

public class TerrainGen
{
    private const int HEIGHT_OFFSET = 64;
    private const int BEDROCK_HEIGHT = 3;

    // Stone layer
    private const int STONE_BASE_HEIGHT = -25;
    private const int STONE_MIN_HEIGHT = -12;
    private const int STONE_MOUNTAIN_HEIGHT = 48;
    private const float STONE_MOUNTAIN_FREQUENCY = 0.006f;
    private const float STONE_BASE_NOISE = 0.03f;
    private const int STONE_BASE_NOISE_HEIGHT = 3;

    // Dirt layer
    private const int DIRT_BASE_HEIGHT = 5;
    private const float DIRT_NOISE = 0.025f;
    private const int DIRT_NOISE_HEIGHT = 2;

    // Trees
    private const float TREE_FREQUENCY = 0.2f;
    private const int TREE_DENSITY = 3;
    private const int TREE_BORDER = 3;
    private const int TRUNK_HEIGHT = 6;
    private const int LEAVES_RADIUS = 2;
    private const int LEAVES_MIN_Y = 4;
    private const int LEAVES_MAX_Y = 8;

    // Flowers
    private const float FLOWER_FREQUENCY = 0.3f;
    private const int FLOWER_DENSITY = 5;

    // Grass tufts
    private const float GRASS_TUFT_FREQUENCY = 0.4f;
    private const int GRASS_TUFT_DENSITY = 8;

    private readonly OreConfig[] ORES =
    [
        new() { Type = BlockType.CoalOre,    MinY = 5, MaxY = 80, ChunkChance = 0.8f, VeinsPerChunk = 20, MinRadius = 0.5f, MaxRadius = 1.5f, MinLength = 5, MaxLength = 15 },
        new() { Type = BlockType.IronOre,    MinY = 5, MaxY = 64, ChunkChance = 0.7f, VeinsPerChunk = 15, MinRadius = 0.4f, MaxRadius = 1.2f, MinLength = 4, MaxLength = 12 },
        new() { Type = BlockType.GoldOre,    MinY = 5, MaxY = 32, ChunkChance = 0.3f, VeinsPerChunk = 4,  MinRadius = 0.3f, MaxRadius = 1.0f, MinLength = 3, MaxLength = 8  },
        new() { Type = BlockType.DiamondOre, MinY = 5, MaxY = 16, ChunkChance = 0.1f, VeinsPerChunk = 2,  MinRadius = 0.3f, MaxRadius = 0.8f, MinLength = 2, MaxLength = 6  }
    ];

    public void GenerateChunk(World world, int chunkX, int chunkZ, int seed)
    {
        int originX = chunkX * Chunk.WIDTH;
        int originZ = chunkZ * Chunk.DEPTH;

        GenerateTerrain(world, originX, originZ, seed);
        GenerateOres(world, chunkX, chunkZ, seed);

        var caveCarver = new CaveCarver(originX, originZ, originX + Chunk.WIDTH - 1, originZ + Chunk.DEPTH - 1);
        caveCarver.GenerateCaves(world, chunkX, chunkZ, seed);

        GenerateTrees(world, originX, originZ, chunkX, chunkZ, seed);
        GenerateFlowers(world, originX, originZ, seed);
        
        GenerateGrassTufts(world, originX, originZ, seed);
    }

    private void GenerateTerrain(World world, int originX, int originZ, int seed)
    {
        for (int x = 0; x < Chunk.WIDTH; x++)
        {
            for (int z = 0; z < Chunk.DEPTH; z++)
            {
                int worldX = originX + x;
                int worldZ = originZ + z;
                int stoneHeight = GetStoneHeight(worldX, worldZ, seed);
                int dirtHeight = GetDirtHeight(worldX, worldZ, seed);

                for (int y = 0; y < Chunk.HEIGHT; y++)
                {
                    var block = GetBlockAt(worldX, y, worldZ, stoneHeight, dirtHeight);
                    if (block != BlockType.Air)
                        world.SetBlockDirect(worldX, y, worldZ, block);
                }
            }
        }
    }

    private BlockType GetBlockAt(int worldX, int y, int worldZ, int stoneHeight, int dirtHeight)
    {
        if (y < BEDROCK_HEIGHT)
            return BlockType.Bedrock;

        if (y <= stoneHeight)
            return BlockType.Stone;

        if (y <= dirtHeight)
            return y == dirtHeight ? BlockType.Grass : BlockType.Dirt;

        return BlockType.Air;
    }

    // --- Ore generation ---

    private void GenerateOres(World world, int chunkX, int chunkZ, int seed)
    {
        var rng = new Random(HashSeed(seed + 1000, chunkX, chunkZ));

        foreach (var ore in ORES)
        {
            if (RandFloat01(rng) < ore.ChunkChance)
            {
                for (int i = 0; i < ore.VeinsPerChunk; i++)
                {
                    float startX = chunkX * Chunk.WIDTH + rng.Next(Chunk.WIDTH);
                    float startY = rng.Next(ore.MinY, ore.MaxY);
                    float startZ = chunkZ * Chunk.DEPTH + rng.Next(Chunk.DEPTH);

                    float yaw = RandAngle(rng);
                    float pitch = RandFloat(rng) * MathF.PI * 0.25f;

                    float radius = ore.MinRadius + RandFloat01(rng) * (ore.MaxRadius - ore.MinRadius);
                    int length = ore.MinLength + rng.Next(ore.MaxLength - ore.MinLength);

                    PlaceOreWorm(world, startX, startY, startZ, yaw, pitch, radius, length, ore.Type, rng, ore.MaxRadius);
                }
            }
        }
    }

    private void PlaceOreWorm(World world, float x, float y, float z, float yaw, float pitch,
        float radius, int steps, BlockType oreType, Random rng, float maxRadius)
    {
        for (int step = 0; step < steps; step++)
        {
            PlaceOreSphere(world, x, y, z, radius, oreType);
            Advance(ref x, ref y, ref z, yaw, pitch);

            yaw += RandFloat(rng) * 0.5f;
            pitch += RandFloat(rng) * 0.2f;
            pitch = Math.Clamp(pitch, -0.3f, 0.3f);

            radius += RandFloat(rng) * 0.1f;
            radius = Math.Clamp(radius, 0.2f, maxRadius);
        }
    }

    private void PlaceOreSphere(World world, float cx, float cy, float cz, float radius, BlockType oreType)
    {
        for(var bx = (int)MathF.Floor(cx - radius); bx <= (int)MathF.Ceiling(cx + radius); bx++)
        {
            for(var by = (int)MathF.Floor(cy - radius); by <= (int)MathF.Ceiling(cy + radius); by++)
            {
                for(var bz = (int)MathF.Floor(cz - radius); bz <= (int)MathF.Ceiling(cz + radius); bz++)
                {
                    float dx = bx - cx, dy = by - cy, dz = bz - cz;
                    if (dx * dx + dy * dy + dz * dz <= radius * radius)
                    {
                        var block = world.GetBlock(bx, by, bz);
                        if (block == BlockType.Stone)
                            world.SetBlockDirect(bx, by, bz, oreType);
                    }
                }
            }
        }
    }

    // --- Trees and decoration ---

    private void GenerateTrees(World world, int originX, int originZ, int chunkX, int chunkZ, int seed)
    {
        for (int x = -TREE_BORDER; x < Chunk.WIDTH + TREE_BORDER; x++)
        {
            for (int z = -TREE_BORDER; z < Chunk.DEPTH + TREE_BORDER; z++)
            {
                int worldX = originX + x;
                int worldZ = originZ + z;

                if (GetNoise(worldX, seed, worldZ, TREE_FREQUENCY, 100) < TREE_DENSITY)
                {
                    int surfaceY = GetDirtHeight(worldX, worldZ, seed);
                    CreateTree(world, worldX, surfaceY + 1, worldZ);
                }
            }
        }
    }

    private void GenerateFlowers(World world, int originX, int originZ, int seed)
    {
        for (int x = 0; x < Chunk.WIDTH; x++)
        {
            for (int z = 0; z < Chunk.DEPTH; z++)
            {
                int worldX = originX + x;
                int worldZ = originZ + z;

                if (GetNoise(worldX, seed, worldZ, FLOWER_FREQUENCY, 100) < FLOWER_DENSITY)
                {
                    int surfaceY = GetDirtHeight(worldX, worldZ, seed);
                    int y = surfaceY + 1;
                    if (world.GetBlock(worldX, y, worldZ) == BlockType.Air && world.GetBlock(worldX, surfaceY, worldZ) == BlockType.Grass)
                    {
                        BlockType flowerType = Game.Instance.GameRandom.NextDouble() < .5 ? BlockType.YellowFlower : BlockType.RedFlower;
                        
                        world.SetBlockDirect(worldX, y, worldZ, flowerType);
                    }
                        
                }
            }
        }
    }

    private void GenerateGrassTufts(World world, int originX, int originZ, int seed)
    {
        for (int x = 0; x < Chunk.WIDTH; x++)
        {
            for (int z = 0; z < Chunk.DEPTH; z++)
            {
                int worldX = originX + x;
                int worldZ = originZ + z;

                if (GetNoise(worldX, seed + 1000, worldZ, GRASS_TUFT_FREQUENCY, 100) < GRASS_TUFT_DENSITY)
                {
                    int surfaceY = GetDirtHeight(worldX, worldZ, seed);
                    int y = surfaceY + 1;
                    if (world.GetBlock(worldX, y, worldZ) == BlockType.Air &&
                        world.GetBlock(worldX, surfaceY, worldZ) == BlockType.Grass)
                        world.SetBlockDirect(worldX, y, worldZ, BlockType.GrassTuft);
                }
            }
        }
    }

    private void CreateTree(World world, int x, int y, int z)
    {
        for (int lx = -LEAVES_RADIUS; lx <= LEAVES_RADIUS; lx++)
            for (int ly = LEAVES_MIN_Y; ly <= LEAVES_MAX_Y; ly++)
                for (int lz = -LEAVES_RADIUS; lz <= LEAVES_RADIUS; lz++)
                    world.SetBlockDirect(x + lx, y + ly, z + lz, BlockType.Leaves);

        for (int ty = 0; ty < TRUNK_HEIGHT; ty++)
            world.SetBlockDirect(x, y + ty, z, BlockType.Wood);
    }

    // --- Height helpers ---

    private int GetStoneHeight(int worldX, int worldZ, int seed)
    {
        int height = STONE_BASE_HEIGHT + HEIGHT_OFFSET;
        height += GetNoise(worldX, seed, worldZ, STONE_MOUNTAIN_FREQUENCY, STONE_MOUNTAIN_HEIGHT);
        height = Math.Max(height, STONE_MIN_HEIGHT + HEIGHT_OFFSET);
        height += GetNoise(worldX, seed, worldZ, STONE_BASE_NOISE, STONE_BASE_NOISE_HEIGHT);
        return height;
    }

    private int GetDirtHeight(int worldX, int worldZ, int seed)
    {
        return GetStoneHeight(worldX, worldZ, seed) + DIRT_BASE_HEIGHT
             + GetNoise(worldX, seed, worldZ, DIRT_NOISE, DIRT_NOISE_HEIGHT);
    }

    private static int GetNoise(int x, int y, int z, float scale, int max) =>
        (int)Math.Floor((Noise.Generate(x * scale, y * scale, z * scale) + 1f) * (max / 2f));

    // --- Shared helpers (used by CaveCarver) ---

    internal static void Advance(ref float x, ref float y, ref float z, float yaw, float pitch, float speed = 1.0f)
    {
        x += MathF.Cos(yaw) * MathF.Cos(pitch) * speed;
        y += MathF.Sin(pitch) * speed;
        z += MathF.Sin(yaw) * MathF.Cos(pitch) * speed;
    }

    internal static void Wobble(Random rng, ref float yaw, ref float pitch,
        float yawAmount, float pitchAmount, float pitchClamp)
    {
        yaw += RandFloat(rng) * yawAmount;
        pitch += RandFloat(rng) * pitchAmount;
        pitch = Math.Clamp(pitch, -pitchClamp, pitchClamp);
    }

    internal static float RandFloat(Random rng) => (float)(rng.NextDouble() - 0.5) * 2.0f;
    internal static float RandFloat01(Random rng) => (float)rng.NextDouble();
    internal static float RandAngle(Random rng) => RandFloat01(rng) * MathF.PI * 2.0f;

    internal static int HashSeed(int worldSeed, int cx, int cz)
    {
        return worldSeed ^ (cx * 341873128) ^ (cz * 132897987);
    }
}
