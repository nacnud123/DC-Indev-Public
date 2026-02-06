// Main terrain generation file, uses noise to generate the world | DA | 2/5/26

namespace VoxelEngine.Terrain;

public class TerrainGen
{
    // Terrain height offset
    private const int HEIGHT_OFFSET = 64;
    private const int BEDROCK_HEIGHT = 3;

    // Stone layer parameters
    private const int STONE_BASE_HEIGHT = -25;
    private const int STONE_MIN_HEIGHT = -12;
    private const int STONE_MOUNTAIN_HEIGHT = 48;
    private const float STONE_MOUNTAIN_FREQUENCY = 0.006f;
    private const float STONE_BASE_NOISE = 0.03f;
    private const int STONE_BASE_NOISE_HEIGHT = 3;

    // Dirt/surface layer parameters
    private const int DIRT_BASE_HEIGHT = 5;
    private const float DIRT_NOISE = 0.025f;
    private const int DIRT_NOISE_HEIGHT = 2;

    // Cave parameters
    private const float CAVE_FREQUENCY = 0.025f;
    private const int CAVE_THRESHOLD = 7;
    private const int CAVE_MIN_Y = 5;

    // Tree parameters
    private const float TREE_FREQUENCY = 0.2f;
    private const int TREE_DENSITY = 3;
    private const int TREE_BORDER = 3;
    private const int TRUNK_HEIGHT = 6;
    private const int LEAVES_RADIUS = 2;
    private const int LEAVES_MIN_Y = 4;
    private const int LEAVES_MAX_Y = 8;

    // Flower parameters
    private const float FLOWER_FREQUENCY = 0.3f;
    private const int FLOWER_DENSITY = 5;
    
    // Grass tuft parameters
    private const float GRASS_TUFT_FREQUENCY = 0.4f;
    private const int GRASS_TUFT_DENSITY = 8;

    // Ore parameters
    private const float COAL_FREQUENCY = 0.01f;
    private const int COAL_THRESHOLD = 92;
    private const int COAL_DEPTH = 10;

    public void GenerateChunkBlocks(Chunk chunk, int seed)
    {
        var blocks = GetChunkBlocks(chunk, seed);

        for (int x = 0; x < Chunk.WIDTH; x++)
        {
            for (int y = 0; y < Chunk.HEIGHT; y++)
            {
                for (int z = 0; z < Chunk.DEPTH; z++)
                {
                    chunk.SetBlock(x, y, z, blocks[x, y, z]);
                }
            }
            
        }
            
    }

    public BlockType[,,] GetChunkBlocks(Chunk chunk, int seed)
    {
        var blocks = new BlockType[Chunk.WIDTH, Chunk.HEIGHT, Chunk.DEPTH];

        GenerateTerrain(blocks, chunk, seed);
        GenerateTrees(blocks, chunk, seed);
        GenerateFlowers(blocks, chunk, seed);
        GenerateGrassTufts(blocks, chunk, seed);

        return blocks;
    }

    private void GenerateTerrain(BlockType[,,] blocks, Chunk chunk, int seed)
    {
        for (int x = 0; x < Chunk.WIDTH; x++)
        {
            for (int z = 0; z < Chunk.DEPTH; z++)
            {
                int worldX = chunk.ChunkX * Chunk.WIDTH + x;
                int worldZ = chunk.ChunkZ * Chunk.DEPTH + z;

                int stoneHeight = GetStoneHeight(worldX, worldZ, seed);
                int dirtHeight = GetDirtHeight(worldX, worldZ, seed);

                for (int y = 0; y < Chunk.HEIGHT; y++)
                {
                    blocks[x, y, z] = GetBlockAt(worldX, y, worldZ, stoneHeight, dirtHeight);
                }
            }
        }
    }

    private BlockType GetBlockAt(int worldX, int y, int worldZ, int stoneHeight, int dirtHeight)
    {
        if (y < BEDROCK_HEIGHT)
            return BlockType.Bedrock;

        int caveNoise = GetNoise(worldX, y, worldZ, CAVE_FREQUENCY, 100);
        bool isCave = y > CAVE_MIN_Y && caveNoise <= CAVE_THRESHOLD;
        
        if (y <= stoneHeight)
        {
            if (isCave)
                return BlockType.Air;

            int coalNoise = GetNoise(worldX, y, worldZ, COAL_FREQUENCY, 100);
            
            if (y <= stoneHeight - COAL_DEPTH && coalNoise > COAL_THRESHOLD)
                return BlockType.Sand;

            return BlockType.Stone;
        }
        
        if (y <= dirtHeight)
        {
            if (isCave)
                return BlockType.Air;
            
            return y == dirtHeight ? BlockType.Grass : BlockType.Dirt;
        }
        
        return BlockType.Air;
    }

    private void GenerateTrees(BlockType[,,] blocks, Chunk chunk, int seed)
    {
        for (int x = -TREE_BORDER; x < Chunk.WIDTH + TREE_BORDER; x++)
        {
            for (int z = -TREE_BORDER; z < Chunk.DEPTH + TREE_BORDER; z++)
            {
                int worldX = chunk.ChunkX * Chunk.WIDTH + x;
                int worldZ = chunk.ChunkZ * Chunk.DEPTH + z;

                if (GetNoise(worldX, seed, worldZ, TREE_FREQUENCY, 100) < TREE_DENSITY)
                {
                    int surfaceY = GetDirtHeight(worldX, worldZ, seed);
                    CreateTree(blocks, x, surfaceY + 1, z);
                }
            }
        }
    }

    private void GenerateFlowers(BlockType[,,] blocks, Chunk chunk, int seed)
    {
        for (int x = 0; x < Chunk.WIDTH; x++)
        {
            for (int z = 0; z < Chunk.DEPTH; z++)
            {
                int worldX = chunk.ChunkX * Chunk.WIDTH + x;
                int worldZ = chunk.ChunkZ * Chunk.DEPTH + z;

                if (GetNoise(worldX, seed, worldZ, FLOWER_FREQUENCY, 100) < FLOWER_DENSITY)
                {
                    int surfaceY = GetDirtHeight(worldX, worldZ, seed);
                    int y = surfaceY + 1;

                    if (IsInChunkBounds(x, y, z) && blocks[x, y, z] == BlockType.Air && blocks[x, surfaceY, z] == BlockType.Grass)
                        blocks[x, y, z] = BlockType.Flower;
                }
            }
        }
    }
    
    private void GenerateGrassTufts(BlockType[,,] blocks, Chunk chunk, int seed)
    {
        for (int x = 0; x < Chunk.WIDTH; x++)
        {
            for (int z = 0; z < Chunk.DEPTH; z++)
            {
                int worldX = chunk.ChunkX * Chunk.WIDTH + x;
                int worldZ = chunk.ChunkZ * Chunk.DEPTH + z;

                if (GetNoise(worldX, seed + 1000, worldZ, GRASS_TUFT_FREQUENCY, 100) < GRASS_TUFT_DENSITY)
                {
                    int surfaceY = GetDirtHeight(worldX, worldZ, seed);
                    int y = surfaceY + 1;

                    if (IsInChunkBounds(x, y, z) && blocks[x, y, z] == BlockType.Air && blocks[x, surfaceY, z] == BlockType.Grass)
                        blocks[x, y, z] = BlockType.GrassTuft;
                }
            }
        }
    }

    private void CreateTree(BlockType[,,] blocks, int x, int y, int z)
    {
        for (int lx = -LEAVES_RADIUS; lx <= LEAVES_RADIUS; lx++)
        {
            for (int ly = LEAVES_MIN_Y; ly <= LEAVES_MAX_Y; ly++)
            {
                for (int lz = -LEAVES_RADIUS; lz <= LEAVES_RADIUS; lz++)
                {
                    int bx = x + lx, by = y + ly, bz = z + lz;
                    if (IsInChunkBounds(bx, by, bz))
                        blocks[bx, by, bz] = BlockType.Leaves;
                }
            }
        }
        
        for (int ty = 0; ty < TRUNK_HEIGHT; ty++)
        {
            int by = y + ty;
            if (IsInChunkBounds(x, by, z))
                blocks[x, by, z] = BlockType.Wood;
        }
    }

    private int GetStoneHeight(int worldX, int worldZ, int seed)
    {
        int height = STONE_BASE_HEIGHT+ HEIGHT_OFFSET;
        height += GetNoise(worldX, seed, worldZ, STONE_MOUNTAIN_FREQUENCY, STONE_MOUNTAIN_HEIGHT);

        int minHeight = STONE_MIN_HEIGHT + HEIGHT_OFFSET;
        
        if (height < minHeight)
            height = minHeight;

        height += GetNoise(worldX, seed, worldZ, STONE_BASE_NOISE, STONE_BASE_NOISE_HEIGHT);
        
        return height;
    }

    private int GetDirtHeight(int worldX, int worldZ, int seed)
    {
        int height = GetStoneHeight(worldX, worldZ, seed) + DIRT_BASE_HEIGHT;
        height += GetNoise(worldX, seed, worldZ, DIRT_NOISE, DIRT_NOISE_HEIGHT);
        
        return height;
    }

    private static bool IsInChunkBounds(int x, int y, int z) =>
        x >= 0 && x < Chunk.WIDTH &&
        y >= 0 && y < Chunk.HEIGHT &&
        z >= 0 && z < Chunk.DEPTH;

    private static int GetNoise(int x, int y, int z, float scale, int max) => (int)Math.Floor((Noise.Generate(x * scale, y * scale, z * scale) + 1f) * (max / 2f));
}
