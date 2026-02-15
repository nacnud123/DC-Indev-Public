// Main function that handles loading in functions from the JSON file. Also, placing and exporting blocks as a structure JSON | DA | 2/14/26
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

    public void PlaceRandomly(World world, Structure structure, Vector3i offset)
    {
        var random = new Random();
        int worldWidth = world.SizeInChunks * Chunk.WIDTH;
        int worldDepth = world.SizeInChunks * Chunk.DEPTH;

        int maxX = worldWidth - structure.SizeX;
        int maxZ = worldDepth - structure.SizeZ;
        if (maxX < 0 || maxZ < 0)
            return;

        int x = random.Next(0, maxX + 1);
        int z = random.Next(0, maxZ + 1);

        int groundY = (int)world.FindSpawnPosition(x, z).Y;

        if (groundY + structure.SizeY > Chunk.HEIGHT)
            groundY = Chunk.HEIGHT - structure.SizeY;

        Place(world, structure, x - offset.X, (groundY - 1) - offset.Y, z - offset.Z);
    }

    public void PlaceUnderground(World world, Structure structure, int minY = 10, int maxY = 40,  bool changeRandomBlocks = false, BlockType rndOriginalType = BlockType.Air, BlockType rndNewType = BlockType.Air, float rndChance = 0.0f)
    {
        var random = new Random();
        int worldWidth = world.SizeInChunks * Chunk.WIDTH;
        int worldDepth = world.SizeInChunks * Chunk.DEPTH;

        int maxX = worldWidth - structure.SizeX;
        int maxZ = worldDepth - structure.SizeZ;
        if (maxX < 0 || maxZ < 0)
            return;

        int x = random.Next(0, maxX + 1);
        int z = random.Next(0, maxZ + 1);

        int topY = maxY - structure.SizeY;
        if (topY < minY)
            topY = minY;

        int y = random.Next(minY, topY + 1);

        Place(world, structure, x, y, z, changeRandomBlocks, rndOriginalType, rndNewType, rndChance);
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
