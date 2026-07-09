// Structure loading, placement and exporting from/to JSON files | DA | 2/14/26
using Newtonsoft.Json.Linq;


namespace VoxelEngine.Terrain;

/// <summary>
/// A single block entry within a <see cref="Structure"/>, positioned relative to the structure's own local origin (0,0,0), not world space. Placement adds a world-space origin offset to (X,Y,Z) for every block when stamping the structure into the world.
/// </summary>
public struct StructureBlock
{
    public int X, Y, Z;
    public BlockType Block;
}

/// <summary>
/// An in-memory, loaded prefab: a bounding-box size plus a flat list of relative block placements. Deserialized from a JSON file under Resources/Structures/ by <see cref="StructureLoader.Load"/> and cached there; also the shape produced by <see cref="StructureLoader.Export"/> when the player exports an in-world selection (F1/F2) to a new structure file.
/// </summary>
public class Structure
{
    public string Name;
    public int SizeX, SizeY, SizeZ;
    public List<StructureBlock> Blocks = new();
}

/// <summary>
/// Loads structure JSON files (caching by file name), places them into the world at a given origin (optionally randomizing certain block types on placement), picks placement locations for surface and underground structures, and exports an arbitrary in-world block selection back out to a new JSON structure file. JSON shape: <c>{ "name": string, "size": {"x","y","z"}, "blocks": [[x, y, z, "BlockTypeName"], ...] }</c> where each block entry is a 4-element JSON array (relative coordinates + the BlockType enum name as a string, parsed via Enum.Parse).
/// </summary>
public class StructureLoader
{
    private const string STRUCTURES_PATH = "Resources/Structures/";

    // Cache keyed by file name so repeatedly placing the same structure (e.g. many trees) only parses its JSON once.
    private readonly Dictionary<string, Structure> mCache = new();
    // Drives both random-block substitution during placement and random site selection, so structure placement is deterministic/reproducible per world seed via SeedRandom.
    private Random strucutreRandom = new Random();

    /// <summary>Reseeds the structure RNG (called with the world seed) so structure placement is deterministic per world.</summary>
    public void SeedRandom(int seed)
    {
        strucutreRandom = new Random(seed);
    }

    /// <summary>
    /// Loads and parses a structure JSON file from Resources/Structures/, or returns the cached instance if this file name was already loaded. Each "blocks" entry is a 4-element JSON array: [x, y, z, blockTypeName].
    /// </summary>
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

    /// <summary>
    /// Stamps every block of <paramref name="structure"/> into the world, offsetting each block's local coordinates by (originX, originY, originZ). If <paramref name="changeRandomBlocks"/> is set, any block matching <paramref name="rndOriginalType"/> has an independent <paramref name="rndChance"/> probability of being substituted with <paramref name="rndNewType"/> instead — used for variety (e.g. swapping some leaves/logs for a different variant).
    /// </summary>
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

    // Water level of the world; used as a sanity check to avoid placing surface/underground structures below the ocean (which would bury or flood them).
    private const int SEA_LEVEL = 64;

    /// <summary>
    /// Picks a random (x, z) within the world bounds (up to 50 attempts), finds the terrain height there via <see cref="World.FindSpawnPosition"/>, and rejects sites that are below sea level or whose ground block is water/air (i.e. not solid ground). On the first valid site found, places the structure so its base sits one block into the ground (groundY - 1), offset by <paramref name="offset"/> to align the structure's local origin under that point. Used for surface decoration (e.g. villages, trees) during world generation.
    /// </summary>
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

    /// <summary>
    /// Picks a random (x, z) within the world bounds (up to 50 attempts) that has surface ground at or above sea level (used only as a validity check on the column, not the placement depth), then places the structure at a random Y within [minY, topY] where topY is clamped so the structure's full height fits below <paramref name="maxY"/>. Used for underground features (e.g. dungeons, ore veins as structures) during world generation.
    /// </summary>
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

    /// <summary>
    /// Exports the axis-aligned block region between <paramref name="corner1"/> and <paramref name="corner2"/> (in either order/orientation) to a new JSON structure file under Resources/Structures/, used by the in-game F1/F2 selection-export feature. Block coordinates are re-based relative to the region's minimum corner (so the exported structure's local origin is (0,0,0)), and each block is serialized as [x, y, z, blockTypeName]. Auto-numbers the output file as structure_NNN.json, skipping any names already on disk. Returns the full path written.
    /// </summary>
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
