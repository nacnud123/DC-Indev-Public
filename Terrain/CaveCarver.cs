// Main cave generation function. | DA | 2/14/26
// Caves have four types, Worm, Branching, Cavern, Ravines
using VoxelEngine.Terrain.Blocks;

namespace VoxelEngine.Terrain;

internal class CaveCarver
{
    private const int CAVE_SEARCH_RADIUS = 8;
    private const int CAVE_MIN_Y = 2;
    private const int CAVE_MAX_Y = 120;
    private const int CAVE_SPAWN_MIN_Y = 15;
    private const int CAVE_SPAWN_MAX_Y = 90;
    private const float WORM_CAVE_CHANCE = 0.45f;
    private const float BRANCHING_CAVE_CHANCE = 0.15f;
    private const float CAVERN_CHANCE = 0.08f;
    private const float RAVINE_CHANCE = 0.01f;
    private const int MAX_BRANCH_DEPTH = 3;
    private const float CAVERN_START_CHANCE = 0.008f;

    private readonly int mChunkMinX, mChunkMinZ, mChunkMaxX, mChunkMaxZ;

    public CaveCarver(int chunkMinX, int chunkMinZ, int chunkMaxX, int chunkMaxZ)
    {
        mChunkMinX = chunkMinX;
        mChunkMinZ = chunkMinZ;
        mChunkMaxX = chunkMaxX;
        mChunkMaxZ = chunkMaxZ;
    }

    public void GenerateCaves(World world, int chunkX, int chunkZ, int seed)
    {
        for (int cx = chunkX - CAVE_SEARCH_RADIUS; cx <= chunkX + CAVE_SEARCH_RADIUS; cx++)
        {
            for (int cz = chunkZ - CAVE_SEARCH_RADIUS; cz <= chunkZ + CAVE_SEARCH_RADIUS; cz++)
            {
                var rng = new Random(TerrainGen.HashSeed(seed, cx, cz));

                if (rng.NextDouble() < WORM_CAVE_CHANCE)
                    SpawnWorms(world, cx, cz, rng, CarveWorm);

                if (rng.NextDouble() < BRANCHING_CAVE_CHANCE)
                    SpawnWorms(world, cx, cz, rng, (w, x, y, z, yaw, pitch, r, steps, rand) =>
                        CarveWormBranching(w, x, y, z, yaw, pitch, r, steps, rand, 0));

                if (rng.NextDouble() < CAVERN_CHANCE)
                    SpawnWorms(world, cx, cz, rng, CarveWormCavern);

                if (rng.NextDouble() < RAVINE_CHANCE)
                    CarveRavine(world, cx * Chunk.WIDTH + rng.Next(Chunk.WIDTH),
                        40 + rng.Next(40), cz * Chunk.DEPTH + rng.Next(Chunk.DEPTH), rng);
            }
        }
    }

    // It's like a sphere or worm sort of thing where it carves a sphere, advance forward, wobble direction, then slightly vary radius
    private void SpawnWorms(World world, int cx, int cz, Random rng, Action<World, float, float, float, float, float, float, int, Random> carver)
    {
        int count = 0;

        if (rng.NextDouble() < 0.15)
            count = 1;

        if (rng.NextDouble() < 0.04)
            count = 2;

        for (int i = 0; i < count; i++)
        {
            float startX = cx * Chunk.WIDTH + rng.Next(Chunk.WIDTH);
            float startY = rng.Next(CAVE_SPAWN_MIN_Y, CAVE_SPAWN_MAX_Y);
            float startZ = cz * Chunk.DEPTH + rng.Next(Chunk.DEPTH);
            float yaw = TerrainGen.RandAngle(rng);
            float pitch = TerrainGen.RandFloat(rng) * MathF.PI * 0.25f;
            int length = 64 + rng.Next(64);
            float radius = 1.5f + TerrainGen.RandFloat01(rng) * 2.0f;

            carver(world, startX, startY, startZ, yaw, pitch, radius, length, rng);
        }
    }

    private void CarveWorm(World world, float x, float y, float z, float yaw, float pitch, float radius, int steps, Random rng)
    {
        for (int step = 0; step < steps; step++)
        {
            CarveSphere(world, x, y, z, radius);

            TerrainGen.Advance(ref x, ref y, ref z, yaw, pitch);
            TerrainGen.Wobble(rng, ref yaw, ref pitch, 0.7f, 0.3f, 0.5f);

            radius = Math.Clamp(radius + TerrainGen.RandFloat(rng) * 0.3f, 1.0f, 4.0f);

            if (y < CAVE_MIN_Y || y > CAVE_MAX_Y)
                break;
        }
    }

    // Branching cave, like a worm but can recursively spawn branches. Branches get a diverging yaw/pitch, slightly smaller radius, and half the remaining steps
    private void CarveWormBranching(World world, float x, float y, float z, float yaw, float pitch, float radius, int steps, Random rng, int depth)
    {
        if (depth > MAX_BRANCH_DEPTH)
            return;

        for (int step = 0; step < steps; step++)
        {
            CarveSphere(world, x, y, z, radius);
            TerrainGen.Advance(ref x, ref y, ref z, yaw, pitch);
            TerrainGen.Wobble(rng, ref yaw, ref pitch, 0.7f, 0.3f, 0.5f);
            radius = Math.Clamp(radius + TerrainGen.RandFloat(rng) * 0.3f, 0.8f, 4.0f);

            if (step > 10 && rng.NextDouble() < 0.02)
            {
                float branchYaw = yaw + TerrainGen.RandFloat(rng) * MathF.PI * 0.8f;
                float branchPitch = pitch + TerrainGen.RandFloat(rng) * 0.4f;
                float branchRad = radius * (0.6f + TerrainGen.RandFloat01(rng) * 0.3f);

                CarveWormBranching(world, x, y, z, branchYaw, branchPitch, branchRad, steps / 2 + rng.Next(20), rng, depth + 1);
            }

            if (y < CAVE_MIN_Y || y > CAVE_MAX_Y) 
                break;
        }
    }
    
    private void CarveWormCavern(World world, float x, float y, float z, float yaw, float pitch, float radius, int steps, Random rng)
    {
        bool inCavern = false;
        int cavernTimer = 0, cavernMaxTimer = 0;

        for (int step = 0; step < steps; step++)
        {
            float effectiveRadius = radius;

            if (!inCavern && rng.NextDouble() < CAVERN_START_CHANCE)
            {
                inCavern = true;
                cavernTimer = 0;
                cavernMaxTimer = 8 + rng.Next(12);
            }

            if (inCavern)
            {
                float t = (float)cavernTimer / cavernMaxTimer;
                effectiveRadius = radius + MathF.Sin(t * MathF.PI) * (3.0f + TerrainGen.RandFloat01(rng) * 3.0f);
                if (++cavernTimer >= cavernMaxTimer)
                    inCavern = false;
            }

            CarveSphere(world, x, y, z, effectiveRadius);

            float speed = inCavern ? 0.5f : 1.0f;

            TerrainGen.Advance(ref x, ref y, ref z, yaw, pitch, speed);
            TerrainGen.Wobble(rng, ref yaw, ref pitch, 0.7f, 0.3f, 0.6f);

            if (y < CAVE_MIN_Y || y > CAVE_MAX_Y) 
                break;
        }
    }
    
    // Deep vertical gash, it's like a ravine. Linear path, not worm based
    private void CarveRavine(World world, float startX, float startY, float startZ, Random rng)
    {
        float yaw = TerrainGen.RandAngle(rng);
        int length = 40 + rng.Next(40);
        float x = startX, z = startZ;

        for (int step = 0; step < length; step++)
        {
            float progress = (float)step / length;
            float width = 1.5f + MathF.Sin(progress * MathF.PI) * 3.0f;

            int height = 10 + rng.Next(20);
            int bottomY = (int)(startY - height / 2f);
            int topY = (int)(startY + height / 2f);

            for (int by = bottomY; by <= topY; by++)
            {
                for (int bx = (int)MathF.Floor(x - width); bx <= (int)MathF.Ceiling(x + width); bx++)
                {
                    for (int bz = (int)MathF.Floor(z - width); bz <= (int)MathF.Ceiling(z + width); bz++)
                    {
                        float dx = (bx - x) / width;
                        float dz = (bz - z) / width;
                        if (dx * dx + dz * dz <= 1.0f)
                            CarveBlock(world, bx, by, bz);
                    }
                }
            }

            x += MathF.Cos(yaw);
            z += MathF.Sin(yaw);
            yaw += TerrainGen.RandFloat(rng) * 0.3f;
        }
    }

    private void CarveSphere(World world, float cx, float cy, float cz, float radius)
    {
        float r2 = radius * radius;

        int minX = (int)MathF.Floor(cx - radius), maxX = (int)MathF.Ceiling(cx + radius);
        int minY = (int)MathF.Floor(cy - radius), maxY = (int)MathF.Ceiling(cy + radius);
        int minZ = (int)MathF.Floor(cz - radius), maxZ = (int)MathF.Ceiling(cz + radius);

        for (int bx = minX; bx <= maxX; bx++)
        {
            for (int by = minY; by <= maxY; by++)
            {
                for (int bz = minZ; bz <= maxZ; bz++)
                {
                    float dx = bx - cx, dy = by - cy, dz = bz - cz;

                    if (dx * dx + dy * dy + dz * dz <= r2)
                        CarveBlock(world, bx, by, bz);
                }
            }
        }
    }

    // Set block at position to air, with some checks
    private void CarveBlock(World world, int x, int y, int z)
    {
        if (x < mChunkMinX || x > mChunkMaxX || z < mChunkMinZ || z > mChunkMaxZ)
            return;

        var block = world.GetBlock(x, y, z);
        if (block != BlockType.Bedrock && BlockRegistry.IsSolid(block))
            world.SetBlockDirect(x, y, z, BlockType.Air);
    }
}
