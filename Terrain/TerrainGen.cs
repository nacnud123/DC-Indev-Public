// InDev-style terrain generation. Phases: raising, eroding, soiling, growing, caves, ores, flooding, cave decoration, vegetation. Floating worlds replace soiling with a two-noise carve: the heightmap drives the top surface, and a second noise carves the concave underside, tapering to nothing at world borders. | DA | 2/20/26
namespace VoxelEngine.Terrain;

/// <summary>
/// Drives InDev-style multi-phase world generation for a single chunk at a time. Phases run in a fixed order from <see cref="GenerateChunk"/>: raise (heightmap) -> erode (smoothing) -> soil (stone/dirt/grass layering) -> caves (CaveCarver voids) -> ores (vein scattering) -> flood (water/lava fill) -> cave decoration (mushrooms/webs/clay/gravel) -> grow (trees/flowers/grass tufts). "Raise" and "erode" build a world-wide heightmap once (see EnsureHeightMap) and every other phase operates chunk-local using that shared heightmap. Floating worlds replace the "soil" phase with a two-noise carve (heightmap for the top surface, a second noise for the concave underside) and skip caves/flooding since floating islands have no ocean or below-island interior to carve.
/// </summary>
public class TerrainGen
{
    // World Y at which oceans/lakes are filled and above which land pokes out (also biases sand/coast placement).
    private const int SEA_LEVEL = 64;
    // Bottom N layers are always unbreakable Bedrock, regardless of world type/theme.
    private const int BEDROCK_HEIGHT = 3;
    // World Y below which any exposed air is flooded with lava (creates lava lakes/pockets deep underground).
    private const int LAVA_LEVEL = 10;
    // Vertical distance between stacked floating-world layers (island "shells" repeat every this many blocks).
    private const int LAYER_SPACING = 48;
    // Extra XZ margin outside the chunk that tree placement is allowed to roll positions within, so trees near chunk edges can still be considered (their canopy may still get clipped by the neighbor-block checks below).
    private const int TREE_BORDER = 3;
    // Minimum XZ distance enforced between two trees placed in the same chunk/pass.
    private const int TREE_MIN_SPACING = 4;
    // Divides total world area to get the target tree count; larger divisor = sparser forests.
    private const int TREE_DENSITY_DIVISOR = 200;

    // Raising: two distorted noise pairs blended by a selector. Each "source" noise is domain-warped by its paired "distort" noise (the source is sampled at an X offset driven by the distort noise's output) to break up perfectly regular Perlin contours into more organic, InDev-style terrain shapes.
    private readonly FastNoiseLite mRaise1Source = CreateOctaveNoise(8);
    private readonly FastNoiseLite mRaise1Distort = CreateOctaveNoise(8);
    private readonly FastNoiseLite mRaise2Source = CreateOctaveNoise(8);
    private readonly FastNoiseLite mRaise2Distort = CreateOctaveNoise(8);
    // Decides per-column whether to prefer the "primary" or "alternative" raise noise (creates sharp biome-like transitions between terrain styles rather than a smooth blend).
    private readonly FastNoiseLite mSelectorNoise = CreateOctaveNoise(6);
    // Low-frequency noise used only for Island worlds to perturb the circular edge falloff boundary so coastlines aren't perfectly round.
    private readonly FastNoiseLite mIslandNoise = CreateOctaveNoise(2);

    // Eroding: two distorted noise pairs, following the same domain-warp pattern as the raise noises above, used to selectively smooth/step terrain heights after raising.
    private readonly FastNoiseLite mErode1Source = CreateOctaveNoise(8);
    private readonly FastNoiseLite mErode1Distort = CreateOctaveNoise(8);
    private readonly FastNoiseLite mErode2Source = CreateOctaveNoise(8);
    private readonly FastNoiseLite mErode2Distort = CreateOctaveNoise(8);

    // Varies dirt layer thickness under grass so the stone/dirt boundary isn't perfectly flat.
    private readonly FastNoiseLite mDirtNoise = CreateOctaveNoise(8);
    // Drives the concave underside carve for Floating worlds (see GenerateSoilFloating).
    private readonly FastNoiseLite mCarveNoise = CreateOctaveNoise(8);

    // Total world size in blocks (chunk grid size * chunk dimensions) - defines the heightmap array bounds.
    private int mWorldBlocksX;
    private int mWorldBlocksZ;

    // World-wide heightmap, one entry per (worldX, worldZ) column, lazily built once per world size by EnsureHeightMap and shared by every chunk generation call.
    private int[]? mHeightMap;
    private int mHmWidth, mHmDepth;

    // Floating worlds only: number of stacked island layers and each layer's absolute water-level Y (see EnsureHeightMap/GenerateChunk).
    private int mLayerCount;
    private int[]? mLayerWaterLevels;

    // Floating vegetation: while iterating layers in GenerateChunk, these bias GetHeight()/the vegetation floor so tree/flower/grass placement targets the current layer's surface instead of always the base layer. Reset to 0/SEA_LEVEL once all layers are processed.
    private int mSurfaceYAdjust = 0;
    private int mVegetationFloor = SEA_LEVEL;

    public WorldGenSettings WorldSettings;

    /// <summary>
    /// Frees the cached world-wide heightmap and floating-layer data. Called when a world is unloaded/regenerated so the next EnsureHeightMap call rebuilds from scratch instead of reusing stale data (e.g. if world size changed).
    /// </summary>
    public void ReleaseGenerationData()
    {
        mHeightMap = null;
        mHmWidth = 0;
        mHmDepth = 0;
        mLayerWaterLevels = null;
    }

    /// <summary>
    /// Runs every generation phase for one chunk, in order: ensures the shared heightmap exists, lays down stone/dirt/grass (soil), carves caves (skipped for Floating worlds, which have no solid interior to carve caves into), scatters ore veins, floods water/lava, decorates cave surfaces, then places vegetation. For Floating worlds, vegetation runs once per stacked island layer (adjusting <see cref="mSurfaceYAdjust"/>/<see cref="mVegetationFloor"/> so GetHeight and floor checks target each layer's own surface) instead of once for the whole column.
    /// </summary>
    public void GenerateChunk(World world, int chunkX, int chunkZ, int seed)
    {
        mWorldBlocksX = world.SizeInChunks * Chunk.WIDTH;
        mWorldBlocksZ = world.SizeInChunks * Chunk.DEPTH;

        SetSeed(seed);
        EnsureHeightMap(world, seed);

        int originX = chunkX * Chunk.WIDTH;
        int originZ = chunkZ * Chunk.DEPTH;

        // Phase: soil - lays down stone/dirt/grass columns from the heightmap (or the floating carve variant).
        GenerateSoil(world, originX, originZ);
        // Phase: grow (coastline pass) - converts shoreline grass/dirt into sand/gravel near sea level, plus scattered inland gravel patches.
        GenerateGrowing(world, originX, originZ, seed);

        if (WorldSettings.Type != WorldTye.Floating)
        {
            // Phase: caves - delegates to CaveCarver, scoped to this chunk's world-space block bounds.
            var caveCarver = new CaveCarver(originX, originZ, originX + Chunk.WIDTH - 1, originZ + Chunk.DEPTH - 1);
            caveCarver.GenerateCaves(world, chunkX, chunkZ, seed);
        }

        // Phase: ores - scatters ore veins (coal/iron/gold/diamond) through stone.
        GenerateOres(world, chunkX, chunkZ, seed);
        // Phase: flood - fills exposed air at/below sea level with the theme's ocean fluid, then fills deep air below LAVA_LEVEL with lava.
        FloodWater(world, originX, originZ);
        FloodLava(world, originX, originZ);

        if (WorldSettings.Type != WorldTye.Floating)
            // Phase: cave decoration - sprinkles mushrooms/webs/clay/gravel onto cave surfaces exposed by the caves phase.
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
            // Phase: grow (vegetation) - trees, flowers, grass tufts, for non-Floating worlds.
            GenerateTrees(world, originX, originZ, seed);
            GenerateFlowers(world, originX, originZ, seed);
            GenerateGrassTufts(world, originX, originZ, seed);
        }
    }

    /// <summary>
    /// Phase: raise + erode. Lazily builds (or rebuilds, if world size changed) the world-wide heightmap shared by every chunk. This is where "raise" (base terrain shape via blended, domain-warped Perlin noise, with world-type-specific edge falloff) and "erode" (noise-driven step smoothing) actually happen; every other generation phase just reads the resulting heightmap via <see cref="GetHeight"/>. For Floating worlds, also computes the stacked island layer water levels and biases the heightmap so GetHeight()+SEA_LEVEL lines up with the topmost layer's absolute surface Y.
    /// </summary>
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
                // Sample coordinates scaled up slightly (1.3x) to make the base noise features finer relative to the world grid.
                float fx = x * 1.3f;
                float fz = z * 1.3f;

                // Domain warp: offset the X sample of the "source" noise by the "distort" noise's output (scaled by 10) so straight Perlin contours become wavy/organic.
                double warp1 = mRaise1Distort.GetNoise(fx, fz) * 10.0;
                // "primary" terrain candidate: rescaled/re-centered so its useful range sits roughly around 0 (lower, rolling terrain).
                double primary = mRaise1Source.GetNoise(fx + (float)warp1, fz) * 8.0 / 6.0 - 4.0;

                double warp2 = mRaise2Distort.GetNoise(fx, fz) * 10.0;
                // "alternative" terrain candidate: rescaled and shifted +10 higher than primary, producing taller landmass where selected (mountains/highlands).
                double alternative = mRaise2Source.GetNoise(fx + (float)warp2, fz) * 8.0 / 5.0 + 10.0 - 4.0;

                // Selector noise decides, per-column, whether to fall back to "primary" instead of "alternative" - selector > 0 forces flatter terrain, creating sharp boundaries between highland and lowland regions rather than a smooth gradient.
                double selector = mSelectorNoise.GetNoise((float)x, (float)z) * 6.0 / 8.0;
                if (selector > 0.0)
                    alternative = primary;

                // Take the taller of the two candidates (so highlands "poke through" lowlands) and halve to compress the final height range.
                double height = Math.Max(primary, alternative) / 2.0;

                if (WorldSettings.Type == WorldTye.Inland || WorldSettings.Type == WorldTye.Floating)
                {
                    mHeightMap[x + z * mHmWidth] = (int)height;
                }
                else // Island: squared edge-distance falloff
                {
                    // edgeX/edgeZ: 0 at world center, 1 at world edge (Chebyshev-style normalized distance per axis).
                    double edgeX = Math.Abs((double)x / (mHmWidth - 1.0) - 0.5) * 2.0;
                    double edgeZ = Math.Abs((double)z / (mHmDepth - 1.0) - 0.5) * 2.0;
                    // Euclidean edge distance, scaled down slightly (0.8) so the falloff doesn't fully engage until near the true border.
                    double dist = Math.Sqrt(edgeX * edgeX + edgeZ * edgeZ) * 0.8;

                    // Perturb the falloff radius with low-frequency noise so the coastline isn't a perfect circle/square.
                    double islandMod = mIslandNoise.GetNoise(x * 0.05f, z * 0.05f) / 4.0 + 1.0;
                    dist = Math.Min(dist, islandMod);
                    dist = Math.Max(dist, Math.Max(edgeX, edgeZ));
                    dist = Math.Clamp(dist, 0.0, 1.0);
                    // Square the falloff so it stays near 0 (no falloff) through most of the interior and only ramps up sharply near the border.
                    dist *= dist;

                    // Blend original height toward a sunken (-10 + 5 = -5 net) value as dist -> 1, sinking terrain into the sea near world edges.
                    height = height * (1.0 - dist) - dist * 10.0 + 5.0;
                    if (height < 0.0)
                        // Push already-underwater terrain even deeper (quadratic falloff) for a smoother seabed slope instead of a hard cliff.
                        height -= height * height * 0.2;

                    mHeightMap[x + z * mHmWidth] = (int)height;
                }
            }
        }

        // Eroding: noise-driven step smoothing - ((h - mask) / 2 * 2) + mask. Only applied where a separate "erosionAmount" noise exceeds a threshold, so erosion is patchy rather than uniform across the whole map (mimics InDev's terracing/erosion look).
        for (int z = 0; z < mHmDepth; z++)
        {
            for (int x = 0; x < mHmWidth; x++)
            {
                float fx = x * 2f;
                float fz = z * 2f;

                double warp1 = mErode1Distort.GetNoise(fx, fz);
                double erosionAmount = mErode1Source.GetNoise(fx + (float)(warp1 * 10.0), fz) / 8.0;

                double warp2 = mErode2Distort.GetNoise(fx, fz);
                // mask is 0 or 1 depending on noise sign - used below to bias the integer-division rounding, giving each eroded region a consistent (not alternating) parity/terrace offset.
                int mask = mErode2Source.GetNoise(fx + (float)(warp2 * 10.0), fz) > 0.0 ? 1 : 0;

                if (erosionAmount > 2.0)
                {
                    int h = mHeightMap[x + z * mHmWidth];
                    // Integer division by 2 then back to *2 snaps height to an even number (offset by mask), collapsing fine height variation into 2-block "steps"/terraces - this is the actual erosion/smoothing effect.
                    h = ((h - mask) / 2 * 2) + mask;
                    mHeightMap[x + z * mHmWidth] = h;
                }
            }
        }

        // Floating: compute per-layer water levels, then bias the heightmap so GetHeight()+SEA_LEVEL gives the topmost layer's absolute surface Y.
        if (WorldSettings.Type == WorldTye.Floating)
        {
            // Layers are spaced LAYER_SPACING blocks apart starting 32 below the world ceiling, stacked downward until they'd go below Y=64.
            mLayerCount = (Chunk.HEIGHT - 64) / LAYER_SPACING + 1;
            mLayerWaterLevels = new int[mLayerCount];
            for (int l = 0; l < mLayerCount; l++)
                mLayerWaterLevels[l] = Chunk.HEIGHT - 32 - l * LAYER_SPACING;

            // Shift every heightmap sample so that height 0 (typical sea level) actually corresponds to the topmost layer's water level, keeping the raw noise-driven shape but relocating it in world Y.
            int adjust = mLayerWaterLevels[0] - SEA_LEVEL;
            for (int i = 0; i < mHmWidth * mHmDepth; i++)
                mHeightMap[i] += adjust;
        }
    }

    // Looks up the (possibly layer-adjusted, via mSurfaceYAdjust) heightmap value for a world column. Out-of-bounds columns return a low fallback (SEA_LEVEL - 10) so edge sampling (e.g. from CaveCarver-adjacent logic or tree canopy checks near chunk borders) doesn't index outside the heightmap array.
    private int GetHeight(int worldX, int worldZ)
    {
        if (worldX < 0 || worldX >= mHmWidth || worldZ < 0 || worldZ >= mHmDepth)
            return SEA_LEVEL - 10;

        return mHeightMap![worldX + worldZ * mHmWidth] + mSurfaceYAdjust;
    }

    /// <summary>
    /// Phase: soil. Fills each column of the chunk with Bedrock (bottom layers) / Stone / Dirt / Grass (or Dirt for the Hell theme, which has no grass) up to the heightmap surface, with a noise-varied dirt layer thickness. Delegates to <see cref="GenerateSoilFloating"/> for Floating worlds, which use a different (carved, layered) surface shape.
    /// </summary>
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

                // Heightmap stores an offset from SEA_LEVEL, so add SEA_LEVEL to get the absolute world-Y surface.
                int surfaceHeight = GetHeight(worldX, worldZ) + SEA_LEVEL;

                // Noise output is roughly [-1,1]; /24 + 4 keeps dirt depth centered around ~4 blocks with only mild variation, clamped to at least 1.
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

    /// <summary>
    /// Floating-world variant of the soil phase. Heightmap drives the top surface; mCarveNoise determines the bottom via the InDev var76 formula. Cubic edge falloff (var27) tapers the island to nothing at world borders (so floating islands don't extend into the sky at the world edge). Runs once per stacked layer (see mLayerWaterLevels), each layer generating its own independent island slab bounded above by its water level and below by the carved floor.
    /// </summary>
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
                // Signed sqrt: sqrt(|x|) preserves the original sign but compresses large magnitudes, producing a carve depth that varies more gently than raw noise (avoids jagged undersides), then scaled up by 100 to reach block-scale depths.
                double sqrtCarve = Math.Sqrt(Math.Abs(carveRaw)) * Math.Sign(carveRaw) * 100.0;

                for (int layer = 0; layer < mLayerCount; layer++)
                {
                    int waterLevel = mLayerWaterLevels[layer];
                    int surfaceY = Math.Min(rawH + waterLevel, Chunk.HEIGHT - 1);
                    int stoneTop = surfaceY - dirtDepth;

                    // islandFloorY: island bottom. Lerps from carve noise at center to world height at borders. Positive sqrtCarve means the noise bottom is above waterLevel - fully carve to prevent slivers.
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

    /// <summary>
    /// Phase: grow (coastline pass, despite the "growing" name this does not place vegetation - see GenerateTrees/GenerateFlowers/GenerateGrassTufts for that). Converts grass/dirt near sea level into a coastal block (sand or gravel, per WorldSettings.CoastIsGrass), and adds scattered inland gravel patches on higher terrain. Skipped for Flat and Floating worlds, which have no sea-level coastline to dress.
    /// </summary>
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

                // Skip land columns - any air below sea level there is a cave, not open ocean.
                if (GetHeight(worldX, worldZ) + SEA_LEVEL > SEA_LEVEL)
                    continue;

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

            for (int step = 0; step < 4; step++)
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
                if (rng.NextDouble() > 0.04)
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
