// Main cave generation function. | DA | 2/14/26 Caves have four types, Worm, Branching, Cavern, Ravines
using VoxelEngine.Terrain.Blocks;

namespace VoxelEngine.Terrain;

/// <summary>
/// Carves underground voids (the "caves" phase of TerrainGen) into a single target chunk by scanning a wide radius of neighboring chunk columns for cave "seeds" (deterministic per-column RNG) and, for each seed that rolls a spawn chance, running one of four carve shapes: worm tunnels, branching tunnels, worm-with-caverns, or vertical ravines. Carving from neighboring columns (not just the target chunk) lets tunnels that originate outside the chunk still poke into it, since CarveSphere/CarveBlock silently no-op for any coordinate outside the configured chunk bounds. One CaveCarver instance is scoped to exactly one target chunk (see constructor).
/// </summary>
internal class CaveCarver
{

    // How many chunks out (in each direction) from the target chunk to scan for cave seeds - wide enough that long worm tunnels starting several chunks away can still reach into this chunk.
    private const int CAVE_SEARCH_RADIUS = 8;
    // Hard floor/ceiling Y that any carve operation is clamped to (keeps caves out of the bedrock layer and near the world height cap).
    private const int CAVE_MIN_Y = 2;
    private const int CAVE_MAX_Y = 120;
    private const int CAVE_SPAWN_MIN_Y = 5;         // was 15
    private const int CAVE_SPAWN_MAX_Y = 110;       // was 90
    // Per-column probability [0,1] that this cave type attempts to spawn at all (independent rolls, so multiple types can overlap in one column).
    private const float WORM_CAVE_CHANCE = 0.65f;   // was 0.45f
    private const float BRANCHING_CAVE_CHANCE = 0.30f; // was 0.15f
    private const float CAVERN_CHANCE = 0.12f;      // was 0.08f
    private const float RAVINE_CHANCE = 0.02f;      // was 0.01f
    // Recursion limit for CarveWormBranching so branches-of-branches can't recurse indefinitely.
    private const int MAX_BRANCH_DEPTH = 3;
    // Per-step probability that a cavern-worm transitions into a temporary widened "cavern" bulge.
    private const float CAVERN_START_CHANCE = 0.008f;

    // World-space block bounds of the single chunk this carver is allowed to write into.
    private readonly int mChunkMinX, mChunkMinZ, mChunkMaxX, mChunkMaxZ;
    private Chunk? mTargetChunk;

    /// <summary>
    /// Scopes this carver to a single target chunk, given as an inclusive world-space block AABB (min/max X and Z, full chunk height). All carve operations clip to these bounds.
    /// </summary>
    public CaveCarver(int chunkMinX, int chunkMinZ, int chunkMaxX, int chunkMaxZ)
    {
        mChunkMinX = chunkMinX;
        mChunkMinZ = chunkMinZ;
        mChunkMaxX = chunkMaxX;
        mChunkMaxZ = chunkMaxZ;
    }

    /// <summary>
    /// Entry point for the caves generation phase for one chunk. Scans every chunk column within <see cref="CAVE_SEARCH_RADIUS"/> of (chunkX, chunkZ), seeds a deterministic RNG per column (via TerrainGen.HashSeed so results are stable/reproducible for a given world seed), and independently rolls each of the four cave types for that column. Carve calls that land outside this carver's target chunk are silently clipped (see CarveSphere/CarveBlock).
    /// </summary>
    public void GenerateCaves(World world, int chunkX, int chunkZ, int seed)
    {
        mTargetChunk = world.GetChunk(chunkX, chunkZ);
        if (mTargetChunk == null)
            return;

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

    // It's like a sphere or worm sort of thing where it carves a sphere, advance forward, wobble direction, then slightly vary radius. Rolls 0, 1, or 2 worm starts per column (two independent coin flips), each with a random start position/direction/length/radius, then hands each start off to the given carver delegate (CarveWorm, CarveWormBranching, or CarveWormCavern) to actually walk and carve.
    private void SpawnWorms(World world, int cx, int cz, Random rng, Action<World, float, float, float, float, float, float, int, Random> carver)
    {
        int count = 0;

        if (rng.NextDouble() < 0.40)
            count++;

        if (rng.NextDouble() < 0.12)
            count++;

        for (int i = 0; i < count; i++)
        {
            float startX = cx * Chunk.WIDTH + rng.Next(Chunk.WIDTH);
            float startY = rng.Next(CAVE_SPAWN_MIN_Y, CAVE_SPAWN_MAX_Y);
            float startZ = cz * Chunk.DEPTH + rng.Next(Chunk.DEPTH);
            float yaw = TerrainGen.RandAngle(rng);          // random horizontal facing direction
            float pitch = TerrainGen.RandFloat(rng) * MathF.PI * 0.25f; // shallow initial up/down tilt
            int length = 64 + rng.Next(64);                 // number of carve steps (tunnel length)
            float radius = 1.5f + TerrainGen.RandFloat01(rng) * 2.0f; // starting tunnel radius

            carver(world, startX, startY, startZ, yaw, pitch, radius, length, rng);
        }
    }

    // Basic worm tunnel: at each step, carve a sphere at the current position, then move forward one unit along (yaw, pitch), randomly perturb the direction slightly ("wobble"), and let the radius drift randomly within [1.0, 4.0]. Stops early if it wanders outside the valid Y range.
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

            // After the first 10 steps, each step has a 2% chance to spawn a child branch: a new worm that diverges sharply in yaw/pitch, starts slightly smaller, and recurses with half the remaining step budget (plus jitter) at depth+1, bounded by MAX_BRANCH_DEPTH.
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

    // Worm tunnel that can periodically balloon into a rounded "cavern" chamber. Each step has a small chance (CAVERN_START_CHANCE) to enter a cavern state that lasts 8-19 steps; while active, the carve radius is boosted by a sine-shaped envelope (rises then falls smoothly, peaking mid-cavern) plus extra random bulge, and forward movement speed is halved so the widened carve overlaps itself into a single open chamber rather than a wide tunnel.
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
                // Sine envelope: 0 at cavern start/end, 1 at the midpoint, so the chamber smoothly swells outward and then closes back down instead of snapping to full size.
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

    // Deep vertical gash, it's like a ravine. Linear path, not worm based. Unlike the worm carvers, this walks a straight-ish 2D path (X/Z only, gentle yaw drift) and for every step carves a full vertical column from bottomY to topY - producing a tall, narrow canyon rather than a rounded tunnel. Column width follows a sine envelope so the ravine is narrow at both ends and widest in the middle.
    private void CarveRavine(World world, float startX, float startY, float startZ, Random rng)
    {
        float yaw = TerrainGen.RandAngle(rng);
        int length = 40 + rng.Next(40);
        int height = 10 + rng.Next(20);
        int bottomY = (int)(startY - height / 2f);
        int topY = (int)(startY + height / 2f);
        float x = startX, z = startZ;

        for (int step = 0; step < length; step++)
        {
            float progress = (float)step / length;
            // Sine taper: width is 1.5 at both ends (progress 0 and 1) and peaks at 1.5 + 3.0 = 4.5 at the midpoint.
            float width = 1.5f + MathF.Sin(progress * MathF.PI) * 3.0f;

            for (int by = bottomY; by <= topY; by++)
            {
                for (int bx = (int)MathF.Floor(x - width); bx <= (int)MathF.Ceiling(x + width); bx++)
                {
                    for (int bz = (int)MathF.Floor(z - width); bz <= (int)MathF.Ceiling(z + width); bz++)
                    {
                        // Normalized elliptical (here circular, since both axes divide by the same width) distance check - keeps the cross-section round rather than square.
                        float dx = (bx - x) / width;
                        float dz = (bz - z) / width;
                        if (dx * dx + dz * dz <= 1.0f)
                            CarveBlock(bx, by, bz);
                    }
                }
            }

            x += MathF.Cos(yaw);
            z += MathF.Sin(yaw);
            yaw += TerrainGen.RandFloat(rng) * 0.3f;
        }
    }

    // Carves a filled sphere of air (subject to CarveBlock's solid/bedrock checks) centered at (cx, cy, cz) with the given radius, clipped to this carver's target chunk bounds and the CAVE_MIN_Y/CAVE_MAX_Y range. Used as the basic carve primitive by every worm-based cave type.
    private void CarveSphere(World world, float cx, float cy, float cz, float radius)
    {
        int minX = (int)MathF.Floor(cx - radius), maxX = (int)MathF.Ceiling(cx + radius);
        int minZ = (int)MathF.Floor(cz - radius), maxZ = (int)MathF.Ceiling(cz + radius);

        // Reject spheres entirely outside the chunk before iterating any blocks.
        if (maxX < mChunkMinX || minX > mChunkMaxX || maxZ < mChunkMinZ || minZ > mChunkMaxZ)
            return;

        float r2 = radius * radius;
        int minY = (int)MathF.Floor(cy - radius), maxY = (int)MathF.Ceiling(cy + radius);

        // Clamp iteration to the chunk + valid Y range so we never touch out-of-bounds blocks.
        minX = Math.Max(minX, mChunkMinX);
        maxX = Math.Min(maxX, mChunkMaxX);
        minY = Math.Max(minY, CAVE_MIN_Y);
        maxY = Math.Min(maxY, CAVE_MAX_Y);
        minZ = Math.Max(minZ, mChunkMinZ);
        maxZ = Math.Min(maxZ, mChunkMaxZ);

        if (minY > maxY)
            return;

        for (int bx = minX; bx <= maxX; bx++)
        {
            for (int by = minY; by <= maxY; by++)
            {
                for (int bz = minZ; bz <= maxZ; bz++)
                {
                    float dx = bx - cx, dy = by - cy, dz = bz - cz;

                    if (dx * dx + dy * dy + dz * dz <= r2)
                        CarveBlock(bx, by, bz);
                }
            }
        }
    }

    // Set block at position to air. CarveSphere clamps coordinates to the chunk, so we use direct chunk access here to avoid the full world lookup on every block.
    private void CarveBlock(int x, int y, int z)
    {
        int localX = x - mChunkMinX;
        int localZ = z - mChunkMinZ;

        var block = mTargetChunk!.GetBlock(localX, y, localZ);
        if (block != BlockType.Bedrock && BlockRegistry.IsSolid(block))
            mTargetChunk.SetBlock(localX, y, localZ, BlockType.Air);
    }
}
