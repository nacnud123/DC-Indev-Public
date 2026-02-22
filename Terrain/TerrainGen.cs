// InDev-style terrain generation. Phases: raising, eroding, soiling, growing, caves, ores, flooding, cave decoration, vegetation. Floating worlds replace soiling with a two-noise carve: the heightmap drives the top surface, and a second noise carves the concave underside, tapering to nothing at world borders. | DA | 2/20/26
namespace VoxelEngine.Terrain;

public class TerrainGen
{
    private const int SEA_LEVEL = 64;
    private const int BEDROCK_HEIGHT = 3;
    private const int LAVA_LEVEL = 10;
    private const int LAYER_SPACING = 48;
    private const int TREE_BORDER = 3;
    private const int TREE_MIN_SPACING = 4;
    private const int TREE_DENSITY_DIVISOR = 200;

    // Raising: two distorted noise pairs blended by a selector
    private readonly FastNoiseLite mRaise1Source = CreateOctaveNoise(8);
    private readonly FastNoiseLite mRaise1Distort = CreateOctaveNoise(8);
    private readonly FastNoiseLite mRaise2Source = CreateOctaveNoise(8);
    private readonly FastNoiseLite mRaise2Distort = CreateOctaveNoise(8);
    private readonly FastNoiseLite mSelectorNoise = CreateOctaveNoise(6);
    private readonly FastNoiseLite mIslandNoise = CreateOctaveNoise(2);

    // Eroding: two distorted noise pairs
    private readonly FastNoiseLite mErode1Source = CreateOctaveNoise(8);
    private readonly FastNoiseLite mErode1Distort = CreateOctaveNoise(8);
    private readonly FastNoiseLite mErode2Source = CreateOctaveNoise(8);
    private readonly FastNoiseLite mErode2Distort = CreateOctaveNoise(8);

    private readonly FastNoiseLite mDirtNoise = CreateOctaveNoise(8);
    private readonly FastNoiseLite mCarveNoise = CreateOctaveNoise(8);

    private int mWorldBlocksX;
    private int mWorldBlocksZ;

    private int[]? mHeightMap;
    private int mHmWidth, mHmDepth;

    private int mLayerCount;
    private int[]? mLayerWaterLevels;

    // Floating vegetation
    private int mSurfaceYAdjust = 0;
    private int mVegetationFloor = SEA_LEVEL;

    public WorldGenSettings WorldSettings;

    public void ReleaseGenerationData()
    {
        mHeightMap = null;
        mHmWidth = 0;
        mHmDepth = 0;
        mLayerWaterLevels = null;
    }

    public void GenerateChunk(World world, int chunkX, int chunkZ, int seed)
    {
        mWorldBlocksX = world.SizeInChunks * Chunk.WIDTH;
        mWorldBlocksZ = world.SizeInChunks * Chunk.DEPTH;

        SetSeed(seed);
        EnsureHeightMap(world, seed);

        int originX = chunkX * Chunk.WIDTH;
        int originZ = chunkZ * Chunk.DEPTH;

        GenerateSoil(world, originX, originZ);
        GenerateGrowing(world, originX, originZ, seed);

        if (WorldSettings.Type != WorldTye.Floating)
        {
            var caveCarver = new CaveCarver(originX, originZ, originX + Chunk.WIDTH - 1, originZ + Chunk.DEPTH - 1);
            caveCarver.GenerateCaves(world, chunkX, chunkZ, seed);
        }

        GenerateOres(world, chunkX, chunkZ, seed);
        FloodWater(world, originX, originZ);
        FloodLava(world, originX, originZ);

        if (WorldSettings.Type != WorldTye.Floating)
            DecorateCaves(world, originX, originZ, seed);

        if (WorldSettings.Type == WorldTye.Floating)
        {
            // Vegetation runs per layer; GetHeight is shifted to each layer's absolute surface.
            for (int layer = 0; layer < mLayerCount; layer++)
            {
                mSurfaceYAdjust = mLayerWaterLevels![layer] - mLayerWaterLevels[0];
                mVegetationFloor = mLayerWaterLevels[layer];
                GenerateTrees(world, originX, originZ, seed + layer * 19583);
                GenerateFlowers(world, originX, originZ, seed + layer * 19583);
                GenerateGrassTufts(world, originX, originZ, seed + layer * 19583);
            }
            mSurfaceYAdjust = 0;
            mVegetationFloor = SEA_LEVEL;
        }
        else
        {
            GenerateTrees(world, originX, originZ, seed);
            GenerateFlowers(world, originX, originZ, seed);
            GenerateGrassTufts(world, originX, originZ, seed);
        }
    }

    private void EnsureHeightMap(World world, int seed)
    {
        if (mHeightMap != null && mHmWidth == mWorldBlocksX && mHmDepth == mWorldBlocksZ)
            return;

        mHmWidth = mWorldBlocksX;
        mHmDepth = mWorldBlocksZ;
        mHeightMap = new int[mHmWidth * mHmDepth];

        if (WorldSettings.Type == WorldTye.Flat)
            return;

        // Raising. Floating uses Inland-style (no edge falloff), island shape comes from the soiling carve.
        for (int z = 0; z < mHmDepth; z++)
        {
            for (int x = 0; x < mHmWidth; x++)
            {
                float fx = x * 1.3f;
                float fz = z * 1.3f;

                double warp1 = mRaise1Distort.GetNoise(fx, fz) * 10.0;
                double primary = mRaise1Source.GetNoise(fx + (float)warp1, fz) * 8.0 / 6.0 - 4.0;

                double warp2 = mRaise2Distort.GetNoise(fx, fz) * 10.0;
                double alternative = mRaise2Source.GetNoise(fx + (float)warp2, fz) * 8.0 / 5.0 + 10.0 - 4.0;

                double selector = mSelectorNoise.GetNoise((float)x, (float)z) * 6.0 / 8.0;
                if (selector > 0.0)
                    alternative = primary;

                double height = Math.Max(primary, alternative) / 2.0;

                if (WorldSettings.Type == WorldTye.Inland || WorldSettings.Type == WorldTye.Floating)
                {
                    mHeightMap[x + z * mHmWidth] = (int)height;
                }
                else // Island: squared edge-distance falloff
                {
                    double edgeX = Math.Abs((double)x / (mHmWidth - 1.0) - 0.5) * 2.0;
                    double edgeZ = Math.Abs((double)z / (mHmDepth - 1.0) - 0.5) * 2.0;
                    double dist = Math.Sqrt(edgeX * edgeX + edgeZ * edgeZ) * 0.8;

                    double islandMod = mIslandNoise.GetNoise(x * 0.05f, z * 0.05f) / 4.0 + 1.0;
                    dist = Math.Min(dist, islandMod);
                    dist = Math.Max(dist, Math.Max(edgeX, edgeZ));
                    dist = Math.Clamp(dist, 0.0, 1.0);
                    dist *= dist;

                    height = height * (1.0 - dist) - dist * 10.0 + 5.0;
                    if (height < 0.0)
                        height -= height * height * 0.2;

                    mHeightMap[x + z * mHmWidth] = (int)height;
                }
            }
        }

        // Eroding: noise-driven step smoothing — ((h - mask) / 2 * 2) + mask
        for (int z = 0; z < mHmDepth; z++)
        {
            for (int x = 0; x < mHmWidth; x++)
            {
                float fx = x * 2f;
                float fz = z * 2f;

                double warp1 = mErode1Distort.GetNoise(fx, fz);
                double erosionAmount = mErode1Source.GetNoise(fx + (float)(warp1 * 10.0), fz) / 8.0;

                double warp2 = mErode2Distort.GetNoise(fx, fz);
                int mask = mErode2Source.GetNoise(fx + (float)(warp2 * 10.0), fz) > 0.0 ? 1 : 0;

                if (erosionAmount > 2.0)
                {
                    int h = mHeightMap[x + z * mHmWidth];
                    h = ((h - mask) / 2 * 2) + mask;
                    mHeightMap[x + z * mHmWidth] = h;
                }
            }
        }

        // Floating: compute per-layer water levels, then bias the heightmap so GetHeight()+SEA_LEVEL gives the topmost layer's absolute surface Y.
        if (WorldSettings.Type == WorldTye.Floating)
        {
            mLayerCount = (Chunk.HEIGHT - 64) / LAYER_SPACING + 1;
            mLayerWaterLevels = new int[mLayerCount];
            for (int l = 0; l < mLayerCount; l++)
                mLayerWaterLevels[l] = Chunk.HEIGHT - 32 - l * LAYER_SPACING;

            int adjust = mLayerWaterLevels[0] - SEA_LEVEL;
            for (int i = 0; i < mHmWidth * mHmDepth; i++)
                mHeightMap[i] += adjust;
        }
    }

    private int GetHeight(int worldX, int worldZ)
    {
        if (worldX < 0 || worldX >= mHmWidth || worldZ < 0 || worldZ >= mHmDepth)
            return SEA_LEVEL - 10;

        return mHeightMap![worldX + worldZ * mHmWidth] + mSurfaceYAdjust;
    }

    private void GenerateSoil(World world, int originX, int originZ)
    {
        if (WorldSettings.Type == WorldTye.Floating)
        {
            GenerateSoilFloating(world, originX, originZ);
            return;
        }

        for (int x = 0; x < Chunk.WIDTH; x++)
        {
            for (int z = 0; z < Chunk.DEPTH; z++)
            {
                int worldX = originX + x;
                int worldZ = originZ + z;

                int surfaceHeight = GetHeight(worldX, worldZ) + SEA_LEVEL;

                float dirtVar = mDirtNoise.GetNoise(worldX * 0.5f, worldZ * 0.5f);
                int dirtDepth = (int)(dirtVar / 24.0 + 4.0);
                if (dirtDepth < 1) dirtDepth = 1;

                int stoneTop = surfaceHeight - dirtDepth;

                for (int y = 0; y < Chunk.HEIGHT; y++)
                {
                    BlockType block;

                    if (y < BEDROCK_HEIGHT)
                        block = BlockType.Bedrock;
                    else if (y <= stoneTop)
                        block = BlockType.Stone;
                    else if (y < surfaceHeight)
                        block = BlockType.Dirt;
                    else if (y == surfaceHeight)
                        block = WorldSettings.Theme == WorldTheme.Hell ? BlockType.Dirt : BlockType.Grass;
                    else
                        continue;

                    world.SetBlockDirect(worldX, y, worldZ, block);
                }
            }
        }
    }

    // Heightmap drives the top surface; _carveNoise determines the bottom via the Indev var76 formula. Cubic edge falloff (var27) tapers the island to nothing at world borders.
    private void GenerateSoilFloating(World world, int originX, int originZ)
    {
        // Undo the vegetation bias applied in EnsureHeightMap to recover the raw heightmap value.
        int adjust = mLayerWaterLevels![0] - SEA_LEVEL;

        for (int x = 0; x < Chunk.WIDTH; x++)
        {
            for (int z = 0; z < Chunk.DEPTH; z++)
            {
                int worldX = originX + x;
                int worldZ = originZ + z;

                if (worldX < 0 || worldX >= mHmWidth || worldZ < 0 || worldZ >= mHmDepth)
                    continue;

                int rawH = mHeightMap![worldX + worldZ * mHmWidth] - adjust;

                float dirtVar = mDirtNoise.GetNoise(worldX * 0.5f, worldZ * 0.5f);
                int dirtDepth = (int)(dirtVar / 24.0 + 4.0);

                if (dirtDepth < 1)
                    dirtDepth = 1;

                // 0 at world center, 1 at world border.
                double normX = ((double)worldX / (mHmWidth - 1) - 0.5) * 2.0;
                double normZ = ((double)worldZ / (mHmDepth - 1) - 0.5) * 2.0;
                double var27 = Math.Pow(Math.Max(Math.Abs(normX), Math.Abs(normZ)), 3.0);

                double carveRaw = mCarveNoise.GetNoise(worldX * 2.3f, worldZ * 2.3f) / 24.0;
                double sqrtCarve = Math.Sqrt(Math.Abs(carveRaw)) * Math.Sign(carveRaw) * 100.0;

                for (int layer = 0; layer < mLayerCount; layer++)
                {
                    int waterLevel = mLayerWaterLevels[layer];
                    int surfaceY = Math.Min(rawH + waterLevel, Chunk.HEIGHT - 1);
                    int stoneTop = surfaceY - dirtDepth;

                    // islandFloorY: island bottom. Lerps from carve noise at center to world height at borders. Positive sqrtCarve means the noise bottom is above waterLevel — fully carve to prevent slivers.
                    double carveBase = sqrtCarve + waterLevel;
                    int islandFloorY = (int)(carveBase * (1.0 - var27) + var27 * Chunk.HEIGHT);

                    if (islandFloorY > waterLevel)
                        islandFloorY = Chunk.HEIGHT;

                    if (islandFloorY >= surfaceY)
                        continue;

                    for (int y = islandFloorY; y <= surfaceY; y++)
                    {
                        BlockType block;

                        if (y <= stoneTop)
                            block = BlockType.Stone;
                        else if (y < surfaceY)
                            block = BlockType.Dirt;
                        else
                            block = WorldSettings.Theme == WorldTheme.Hell ? BlockType.Dirt : BlockType.Grass;

                        world.SetBlockDirect(worldX, y, worldZ, block);
                    }
                }
            }
        }
    }

    private void GenerateGrowing(World world, int originX, int originZ, int seed)
    {
        if (WorldSettings.Type == WorldTye.Flat || WorldSettings.Type == WorldTye.Floating)
            return;

        var rng = new Random(HashSeed(seed + 2222, originX / Chunk.WIDTH, originZ / Chunk.DEPTH));

        int sandMin = SEA_LEVEL + WorldSettings.SandNoiseThreshold;
        int sandMax = SEA_LEVEL + WorldSettings.SandBorderYOffset;

        BlockType coastBlock = WorldSettings.CoastIsGrass ? BlockType.Gravel : BlockType.Sand;

        for (int x = 0; x < Chunk.WIDTH; x++)
        {
            for (int z = 0; z < Chunk.DEPTH; z++)
            {
                int worldX = originX + x;
                int worldZ = originZ + z;

                int surfaceHeight = GetHeight(worldX, worldZ) + SEA_LEVEL;

                if (surfaceHeight >= sandMin && surfaceHeight <= sandMax)
                {
                    var surfBlock = world.GetBlock(worldX, surfaceHeight, worldZ);
                    if (surfBlock == BlockType.Grass || surfBlock == BlockType.Dirt)
                    {
                        for (int y = surfaceHeight; y >= Math.Max(surfaceHeight - 3, BEDROCK_HEIGHT); y--)
                        {
                            var b = world.GetBlock(worldX, y, worldZ);
                            if (b == BlockType.Grass || b == BlockType.Dirt)
                                world.SetBlockDirect(worldX, y, worldZ, coastBlock);
                            else
                                break;
                        }
                    }
                }

                if (surfaceHeight > SEA_LEVEL && rng.NextDouble() < 0.02)
                {
                    var surfBlock = world.GetBlock(worldX, surfaceHeight, worldZ);
                    if (surfBlock == BlockType.Grass || surfBlock == BlockType.Dirt)
                    {
                        for (int y = surfaceHeight; y >= Math.Max(surfaceHeight - 2, BEDROCK_HEIGHT); y--)
                        {
                            var b = world.GetBlock(worldX, y, worldZ);
                            if (b == BlockType.Grass || b == BlockType.Dirt)
                                world.SetBlockDirect(worldX, y, worldZ, BlockType.Gravel);
                            else
                                break;
                        }
                    }
                }
            }
        }
    }

    // Ore distribution: worm-based veins with momentum angles and bell-curve radius.
    private void GenerateOres(World world, int chunkX, int chunkZ, int seed)
    {
        var rng = new Random(HashSeed(seed + 1000, chunkX, chunkZ));

        int worldVolume = mWorldBlocksX * mWorldBlocksZ * Chunk.HEIGHT;
        int totalChunks = worldVolume / 256 / 64;
        int chunkCount = (mWorldBlocksX / Chunk.WIDTH) * (mWorldBlocksZ / Chunk.DEPTH);

        GenerateOreType(world, chunkX, chunkZ, rng, BlockType.CoalOre, 1000, 10, (Chunk.HEIGHT * 4) / 5, totalChunks, chunkCount);
        GenerateOreType(world, chunkX, chunkZ, rng, BlockType.IronOre, 800, 8, (Chunk.HEIGHT * 3) / 5, totalChunks, chunkCount);
        GenerateOreType(world, chunkX, chunkZ, rng, BlockType.GoldOre, 500, 6, (Chunk.HEIGHT * 2) / 5, totalChunks, chunkCount);
        GenerateOreType(world, chunkX, chunkZ, rng, BlockType.DiamondOre, 800, 2, Chunk.HEIGHT / 5, totalChunks, chunkCount);
    }

    private void GenerateOreType(World world, int chunkX, int chunkZ, Random rng,
        BlockType oreType, int abundance, int sizeScale, int maxY, int totalChunks, int chunkCount)
    {
        int totalVeins = totalChunks * abundance / 100;
        int veinsPerChunk = Math.Max(1, totalVeins / chunkCount);

        for (int i = 0; i < veinsPerChunk; i++)
        {
            float startX = chunkX * Chunk.WIDTH + rng.Next(Chunk.WIDTH);
            float startY = rng.Next(Math.Max(1, maxY));
            float startZ = chunkZ * Chunk.DEPTH + rng.Next(Chunk.DEPTH);

            int length = (int)(((float)rng.NextDouble() + (float)rng.NextDouble()) * 75.0f * sizeScale / 100.0f);
            if (length < 1) length = 1;

            float yaw = (float)rng.NextDouble() * MathF.PI * 2.0f;
            float yawVel = 0f;
            float pitch = (float)rng.NextDouble() * MathF.PI * 2.0f;
            float pitchVel = 0f;

            float x = startX, y = startY, z = startZ;

            for (int step = 0; step < length; step++)
            {
                x += MathF.Sin(yaw) * MathF.Cos(pitch);
                z += MathF.Cos(yaw) * MathF.Cos(pitch);
                y += MathF.Sin(pitch);

                yaw += yawVel * 0.2f;
                yawVel *= 0.9f;
                yawVel += (float)rng.NextDouble() - (float)rng.NextDouble();

                pitch += pitchVel * 0.5f;
                pitch *= 0.5f;
                pitchVel *= 0.9f;
                pitchVel += (float)rng.NextDouble() - (float)rng.NextDouble();

                float radius = MathF.Sin((float)step * MathF.PI / (float)length)
                    * (float)sizeScale / 100.0f + 1.0f;

                // Flattened ellipsoid: Y weighted 2x to keep veins horizontal.
                for (int bx = (int)(x - radius); bx <= (int)(x + radius); bx++)
                {
                    for (int by = (int)(y - radius); by <= (int)(y + radius); by++)
                    {
                        for (int bz = (int)(z - radius); bz <= (int)(z + radius); bz++)
                        {
                            float dx = bx - x, dy = by - y, dz = bz - z;
                            float dist = dx * dx + dy * dy * 2.0f + dz * dz;
                            if (dist < radius * radius && world.GetBlock(bx, by, bz) == BlockType.Stone)
                                world.SetBlockDirect(bx, by, bz, oreType);
                        }
                    }
                }
            }
        }
    }

    private void FloodWater(World world, int originX, int originZ)
    {
        if (WorldSettings.Type == WorldTye.Floating)
            return;

        BlockType fluid = WorldSettings.OceanFluid;

        for (int x = 0; x < Chunk.WIDTH; x++)
        {
            for (int z = 0; z < Chunk.DEPTH; z++)
            {
                int worldX = originX + x;
                int worldZ = originZ + z;

                for (int y = SEA_LEVEL; y >= BEDROCK_HEIGHT; y--)
                {
                    var block = world.GetBlock(worldX, y, worldZ);
                    if (block == BlockType.Air)
                        world.SetBlockDirect(worldX, y, worldZ, fluid);
                    else
                        break;
                }
            }
        }
    }

    private void FloodLava(World world, int originX, int originZ)
    {
        if (WorldSettings.Type == WorldTye.Floating)
            return;

        for (int x = 0; x < Chunk.WIDTH; x++)
        {
            for (int z = 0; z < Chunk.DEPTH; z++)
            {
                int worldX = originX + x;
                int worldZ = originZ + z;

                for (int y = LAVA_LEVEL; y >= BEDROCK_HEIGHT; y--)
                {
                    var block = world.GetBlock(worldX, y, worldZ);
                    if (block == BlockType.Air)
                        world.SetBlockDirect(worldX, y, worldZ, BlockType.Lava);
                }
            }
        }

    }

    private void DecorateCaves(World world, int originX, int originZ, int seed)
    {
        var rng = new Random(HashSeed(seed + 3333, originX / Chunk.WIDTH, originZ / Chunk.DEPTH));

        for (int x = 0; x < Chunk.WIDTH; x++)
        {
            for (int z = 0; z < Chunk.DEPTH; z++)
            {
                int worldX = originX + x;
                int worldZ = originZ + z;
                int surfaceHeight = GetHeight(worldX, worldZ) + SEA_LEVEL;

                for (int y = BEDROCK_HEIGHT; y < surfaceHeight; y++)
                {
                    var block = world.GetBlock(worldX, y, worldZ);
                    var above = world.GetBlock(worldX, y + 1, worldZ);
                    var below = y > 0 ? world.GetBlock(worldX, y - 1, worldZ) : BlockType.Bedrock;

                    if (block == BlockType.Air)
                    {
                        bool solidBelow = below == BlockType.Stone || below == BlockType.Dirt || below == BlockType.CobbleStone;
                        bool solidAbove = above != BlockType.Air && above != BlockType.Water && above != BlockType.Lava;

                        if (solidBelow && solidAbove)
                        {
                            double roll = rng.NextDouble();
                            if (roll < 0.03)
                                world.SetBlockDirect(worldX, y, worldZ, BlockType.BrownMushroom);
                            else if (roll < 0.045)
                                world.SetBlockDirect(worldX, y, worldZ, BlockType.RedMushroom);
                        }

                        if (above == BlockType.Stone && below == BlockType.Air && rng.NextDouble() < 0.01)
                            world.SetBlockDirect(worldX, y, worldZ, BlockType.SpiderWeb);
                    }
                    else if (block == BlockType.Stone)
                    {
                        if (above == BlockType.Water && rng.NextDouble() < 0.08)
                            world.SetBlockDirect(worldX, y, worldZ, BlockType.Clay);

                        if (below == BlockType.Air && rng.NextDouble() < 0.02)
                            world.SetBlockDirect(worldX, y, worldZ, BlockType.Gravel);
                    }
                }
            }
        }
    }

    private void GenerateTrees(World world, int originX, int originZ, int seed)
    {
        for (int pass = 0; pass < WorldSettings.TreePasses; pass++)
        {
            GenerateTreePass(world, originX, originZ, seed + pass * 31337);
        }
    }

    private void GenerateTreePass(World world, int originX, int originZ, int seed)
    {
        int totalTrees = (mWorldBlocksX * mWorldBlocksZ) / TREE_DENSITY_DIVISOR;
        int chunkCount = (mWorldBlocksX / Chunk.WIDTH) * (mWorldBlocksZ / Chunk.DEPTH);

        // Tree count per chunk is fractional
        float expected = chunkCount > 0 ? (float)totalTrees / chunkCount : 0f;

        var rng = new Random(HashSeed(seed + 9999, originX / Chunk.WIDTH, originZ / Chunk.DEPTH));

        int treesToPlace = (int)expected;

        if (rng.NextDouble() < expected - treesToPlace)
            treesToPlace++;

        treesToPlace = Math.Min(treesToPlace, 2);

        var placedTrees = new List<(int x, int z)>();
        int placedCount = 0;
        int attemptBudget = treesToPlace * 20;

        for (int attempt = 0; attempt < attemptBudget && placedCount < treesToPlace; attempt++)
        {
            int x = originX + rng.Next(-TREE_BORDER, Chunk.WIDTH + TREE_BORDER);
            int z = originZ + rng.Next(-TREE_BORDER, Chunk.DEPTH + TREE_BORDER);

            int surfaceHeight = GetHeight(x, z) + SEA_LEVEL;
            if (surfaceHeight < mVegetationFloor)
                continue;

            var surfaceBlock = world.GetBlock(x, surfaceHeight, z);
            bool validSurface = surfaceBlock == BlockType.Grass ||
                                (WorldSettings.Theme == WorldTheme.Hell && surfaceBlock == BlockType.Dirt);
            if (!validSurface)
                continue;

            bool tooClose = false;
            foreach (var (tx, tz) in placedTrees)
            {
                int dx = x - tx, dz = z - tz;
                if (dx * dx + dz * dz < TREE_MIN_SPACING * TREE_MIN_SPACING)
                {
                    tooClose = true;
                    break;
                }
            }
            if (tooClose)
                continue;

            int treeHeight = rng.Next(3) + 4;

            bool canPlace = true;
            for (int cy = surfaceHeight + 1; cy <= surfaceHeight + treeHeight + 1 && canPlace; cy++)
            {
                int checkRadius = cy >= surfaceHeight + treeHeight - 1 ? 2 : cy == surfaceHeight + 1 ? 0 : 1;
                for (int cx = x - checkRadius; cx <= x + checkRadius && canPlace; cx++)
                    for (int cz = z - checkRadius; cz <= z + checkRadius && canPlace; cz++)
                    {
                        var b = world.GetBlock(cx, cy, cz);
                        if (b != BlockType.Air && b != BlockType.Leaves)
                            canPlace = false;
                    }
            }
            if (!canPlace)
                continue;

            int baseY = surfaceHeight + 1;

            // Canopy: width decreases toward top, corners randomly skipped.
            for (int ly = baseY - 3 + treeHeight; ly <= baseY + treeHeight; ly++)
            {
                int layerOffset = ly - (baseY + treeHeight);
                int radius = 1 - layerOffset / 2;

                for (int lx = x - radius; lx <= x + radius; lx++)
                    for (int lz = z - radius; lz <= z + radius; lz++)
                    {
                        int dx = lx - x, dz = lz - z;
                        if (Math.Abs(dx) == radius && Math.Abs(dz) == radius)
                        {
                            if (rng.Next(2) == 0 || layerOffset == 0)
                                continue;
                        }
                        if (world.GetBlock(lx, ly, lz) == BlockType.Air)
                            world.SetBlockDirect(lx, ly, lz, BlockType.Leaves);
                    }
            }

            for (int ty = 0; ty < treeHeight; ty++)
            {
                var existing = world.GetBlock(x, baseY + ty, z);
                if (existing == BlockType.Air || existing == BlockType.Leaves)
                    world.SetBlockDirect(x, baseY + ty, z, BlockType.Wood);
            }

            placedTrees.Add((x, z));
            placedCount++;
        }
    }

    private void GenerateFlowers(World world, int originX, int originZ, int seed)
    {
        var rng = new Random(HashSeed(seed + 7777, originX / Chunk.WIDTH, originZ / Chunk.DEPTH));
        PlaceVegetationClusters(world, rng, originX, originZ, BlockType.YellowFlower, 1);
        PlaceVegetationClusters(world, rng, originX, originZ, BlockType.RedFlower, 1);
    }

    // Cluster-based random walk: each step wanders ±4 XZ and snaps to the surface.
    private void PlaceVegetationClusters(World world, Random rng, int originX, int originZ, BlockType plantType, int clusterAttempts)
    {
        for (int c = 0; c < clusterAttempts; c++)
        {
            int startX = originX + rng.Next(Chunk.WIDTH);
            int startZ = originZ + rng.Next(Chunk.DEPTH);
            int startY = GetHeight(startX, startZ) + SEA_LEVEL + 1;

            int cx = startX, cy = startY, cz = startZ;

            for (int step = 0; step < 10; step++)
            {
                cx += rng.Next(4) - rng.Next(4);
                cy += rng.Next(2) - rng.Next(2);
                cz += rng.Next(4) - rng.Next(4);

                int surfaceY = GetHeight(cx, cz) + SEA_LEVEL;
                if (cy != surfaceY + 1)
                    cy = surfaceY + 1;

                if (world.GetBlock(cx, cy, cz) == BlockType.Air &&
                    world.GetBlock(cx, cy - 1, cz) is BlockType.Grass or BlockType.Dirt)
                    world.SetBlockDirect(cx, cy, cz, plantType);
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

                var rng = new Random(HashSeed(seed + 5555, worldX, worldZ));
                if (rng.NextDouble() > 0.12)
                    continue;

                int surfaceY = GetHeight(worldX, worldZ) + SEA_LEVEL;
                int y = surfaceY + 1;

                if (world.GetBlock(worldX, y, worldZ) == BlockType.Air &&
                    world.GetBlock(worldX, surfaceY, worldZ) == BlockType.Grass)
                    world.SetBlockDirect(worldX, y, worldZ, BlockType.GrassTuft);
            }
        }
    }

    private static FastNoiseLite CreateOctaveNoise(int octaves)
    {
        var fn = new FastNoiseLite();
        fn.SetNoiseType(FastNoiseLite.NoiseType.Perlin);
        fn.SetFractalType(FastNoiseLite.FractalType.FBm);
        fn.SetFractalOctaves(octaves);
        fn.SetFractalGain(0.5f);
        fn.SetFractalLacunarity(2.0f);
        fn.SetFrequency(0.01f);
        return fn;
    }

    private void SetSeed(int seed)
    {
        mRaise1Source.SetSeed(seed);
        mRaise1Distort.SetSeed(seed + 100);
        mRaise2Source.SetSeed(seed + 200);
        mRaise2Distort.SetSeed(seed + 300);
        mSelectorNoise.SetSeed(seed + 400);
        mIslandNoise.SetSeed(seed + 500);
        mErode1Source.SetSeed(seed + 600);
        mErode1Distort.SetSeed(seed + 700);
        mErode2Source.SetSeed(seed + 800);
        mErode2Distort.SetSeed(seed + 900);
        mDirtNoise.SetSeed(seed + 1000);
        mCarveNoise.SetSeed(seed + 1100);
    }
    
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
    internal static int HashSeed(int worldSeed, int cx, int cz) => worldSeed ^ (cx * 341873128) ^ (cz * 132897987);
}
