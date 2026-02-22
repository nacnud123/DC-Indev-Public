// Structure loading, placement and exporting from/to JSON files | DA | 2/14/26
using Newtonsoft.Json.Linq;
using OpenTK.Mathematics;

namespace VoxelEngine.Terrain;

public struct StructureBlock
{
    public int X, Y, Z;
    public BlockType Block;
}

public class Structure
{
    public string Name;
    public int SizeX, SizeY, SizeZ;
    public List<StructureBlock> Blocks = new();
}

public class StructureLoader
{
    private const string STRUCTURES_PATH = "Resources/Structures/";

    private readonly Dictionary<string, Structure> mCache = new();
    private Random strucutreRandom = new Random();

    public void SeedRandom(int seed)
    {
        strucutreRandom = new Random(seed);
    }

    public Structure Load(string fileName)
    {
        if (mCache.TryGetValue(fileName, out var cached))
            return cached;

        string path = Path.Combine(STRUCTURES_PATH, fileName);
        var json = JObject.Parse(File.ReadAllText(path));

        var size = json["size"]!;
        var structure = new Structure
        {
            Name = json["name"]!.ToString(),
            SizeX = size["x"]!.Value<int>(),
            SizeY = size["y"]!.Value<int>(),
            SizeZ = size["z"]!.Value<int>()
        };

        foreach (JArray arr in json["blocks"]!)
        {
            structure.Blocks.Add(new StructureBlock
            {
                X = arr[0].Value<int>(),
                Y = arr[1].Value<int>(),
                Z = arr[2].Value<int>(),
                Block = Enum.Parse<BlockType>(arr[3].Value<string>())
            });
        }

        mCache[fileName] = structure;
        return structure;
    }

    public void Place(World world, Structure structure, int originX, int originY, int originZ, bool changeRandomBlocks = false, BlockType rndOriginalType = BlockType.Air, BlockType rndNewType = BlockType.Air, float rndChance = 0.0f)
    {
        foreach (var block in structure.Blocks)
        {
            var blockToPlace = block.Block;
            if (changeRandomBlocks)
            {
                if (block.Block == rndOriginalType)
                {
                    if (strucutreRandom.NextDouble() < rndChance)
                    {
                        blockToPlace = rndNewType;
                    }
                }
            }
            
            world.SetBlock(originX + block.X, originY + block.Y, originZ + block.Z, blockToPlace);
            
            
        }
    }

    private const int SEA_LEVEL = 64;

    public void PlaceRandomly(World world, Structure structure, Vector3i offset)
    {
        int worldWidth = world.SizeInChunks * Chunk.WIDTH;
        int worldDepth = world.SizeInChunks * Chunk.DEPTH;

        int maxX = worldWidth - structure.SizeX;
        int maxZ = worldDepth - structure.SizeZ;
        if (maxX < 0 || maxZ < 0)
            return;

        for (int attempt = 0; attempt < 50; attempt++)
        {
            int x = strucutreRandom.Next(0, maxX + 1);
            int z = strucutreRandom.Next(0, maxZ + 1);

            int groundY = (int)world.FindSpawnPosition(x, z).Y;

            if (groundY < SEA_LEVEL)
                continue;

            var groundBlock = world.GetBlock(x, groundY - 1, z);
            if (groundBlock == BlockType.Water || groundBlock == BlockType.Air)
                continue;

            if (groundY + structure.SizeY > Chunk.HEIGHT)
                groundY = Chunk.HEIGHT - structure.SizeY;

            Place(world, structure, x - offset.X, (groundY - 1) - offset.Y, z - offset.Z);
            return;
        }
    }

    public void PlaceUnderground(World world, Structure structure, int minY = 10, int maxY = 40,  bool changeRandomBlocks = false, BlockType rndOriginalType = BlockType.Air, BlockType rndNewType = BlockType.Air, float rndChance = 0.0f)
    {
        int worldWidth = world.SizeInChunks * Chunk.WIDTH;
        int worldDepth = world.SizeInChunks * Chunk.DEPTH;

        int maxX = worldWidth - structure.SizeX;
        int maxZ = worldDepth - structure.SizeZ;
        if (maxX < 0 || maxZ < 0)
            return;

        int topY = maxY - structure.SizeY;
        if (topY < minY)
            topY = minY;

        for (int attempt = 0; attempt < 50; attempt++)
        {
            int x = strucutreRandom.Next(0, maxX + 1);
            int z = strucutreRandom.Next(0, maxZ + 1);

            int groundY = (int)world.FindSpawnPosition(x, z).Y;
            if (groundY < SEA_LEVEL)
                continue;

            int y = strucutreRandom.Next(minY, topY + 1);

            Place(world, structure, x, y, z, changeRandomBlocks, rndOriginalType, rndNewType, rndChance);
            return;
        }
    }

    public static string Export(World world, Vector3i corner1, Vector3i corner2)
    {
        int minX = Math.Min(corner1.X, corner2.X);
        int minY = Math.Min(corner1.Y, corner2.Y);
        int minZ = Math.Min(corner1.Z, corner2.Z);
        int maxX = Math.Max(corner1.X, corner2.X);
        int maxY = Math.Max(corner1.Y, corner2.Y);
        int maxZ = Math.Max(corner1.Z, corner2.Z);

        var blocks = new JArray();
        for (int x = minX; x <= maxX; x++)
        {
            for (int y = minY; y <= maxY; y++)
            {
                for (int z = minZ; z <= maxZ; z++)
                {
                    var block = world.GetBlock(x, y, z);
                    blocks.Add(new JArray(x - minX, y - minY, z - minZ, block.ToString()));
                }
            }
        }

        var json = new JObject
        {
            ["name"] = "",
            ["size"] = new JObject
            {
                ["x"] = maxX - minX + 1,
                ["y"] = maxY - minY + 1,
                ["z"] = maxZ - minZ + 1
            },
            ["blocks"] = blocks
        };

        Directory.CreateDirectory(STRUCTURES_PATH);

        int number = 1;
        while (File.Exists(Path.Combine(STRUCTURES_PATH, $"structure_{number:D3}.json")))
            number++;

        string fileName = $"structure_{number:D3}.json";
        string fullPath = Path.GetFullPath(Path.Combine(STRUCTURES_PATH, fileName));
        File.WriteAllText(fullPath, json.ToString());
        Console.WriteLine($"Exported structure to: {fullPath}");
        return fullPath;
    }
}
