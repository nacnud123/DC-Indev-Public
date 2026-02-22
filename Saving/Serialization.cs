// Saves and loads worlds. Saves the worlds / chunks similarly to how structures are saved and loaded. Also helps give the title screen data like a sorted list of all the worlds | DA | 2/21/26
using System.IO.Compression;
using System.Xml.Serialization;
using VoxelEngine.Core;
using VoxelEngine.Terrain;

namespace VoxelEngine.Saving;

public struct SaveBlock
{
    public int X, Y, Z;
    public BlockType Block;
}

public class Serialization
{
    public static string s_WorldName = "Testing";

    public const string SAVE_LOCATION = "DCIndevSaves";

    public static string SaveLocation()
    {
        string saveLocation = SAVE_LOCATION + "/" + s_WorldName + "/";

        if (!Directory.Exists(saveLocation))
        {
            Directory.CreateDirectory(saveLocation);
        }

        return saveLocation;
    }

    public static string FileName(int x, int z) => $"chunk_{x}_{z}.bin";
    public static string GetWorldDataFile() => "world_info.xml";

    public static WorldSaveData? LoadWorldData(string worldName)
    {
        string metaDataFilePath = Path.Combine(SaveLocation(), GetWorldDataFile());

        if (!File.Exists(metaDataFilePath))
        {
            return null;
        }

        var serializer = new XmlSerializer(typeof(WorldSaveData));
        using (var stream = new FileStream(metaDataFilePath, FileMode.Open))
        {
            return (WorldSaveData)serializer.Deserialize(stream);
        }
    }

    public static void SaveWorldMetadata(WorldSaveData saveData)
    {
        string savePath = SaveLocation();

        string metadataPath = Path.Combine(savePath, GetWorldDataFile());

        var serializer = new XmlSerializer(typeof(WorldSaveData));
        using (var stream = new FileStream(metadataPath, FileMode.Create))
        {
            serializer.Serialize(stream, saveData);
        }
    }

    public static void UpdateLastPlayed(string worldName)
    {
        var worldData = LoadWorldData(worldName);
        if (worldData != null)
        {
            var updatedData = worldData;
            updatedData.LastPlayed = DateTime.Now;

            string oldWorldName = s_WorldName;
            s_WorldName = worldName;

            SaveWorldMetadata(updatedData);
            s_WorldName = oldWorldName;
        }
    }

    public static int GetWorldSize(string worldName)
    {
        var worldData = LoadWorldData(worldName);
        if (worldData != null)
        {
            Console.WriteLine($"World Size for {worldName}: {worldData.WorldSize} chunks");
            return worldData.WorldSize;
        }
        return 0;
    }

    public static WorldSaveData CreateWorld(string worldName, int? customSeed = null, int worldSize = 8, int worldType = 0, int worldTheme = 0, float worldTime = .1f)
    {
        WorldSaveData worldData = new WorldSaveData
        {
            ID = GenerateWorldId(),
            WorldName = worldName,
            Seed = customSeed ?? GenSeed(worldName),
            LastPlayed = DateTime.Now,
            WorldSize = worldSize,

            WorldType = worldType,
            WorldTheme = worldTheme,
            WorldTime = worldTime
        };

        SaveWorldMetadata(worldData);
        return worldData;
    }

    private static int GenerateWorldId()
    {
        return Math.Abs(Guid.NewGuid().GetHashCode());
    }

    private static int GenSeed(string worldName)
    {
        int hash = worldName.GetHashCode();

        var today = DateTime.Today;
        int dateHash = today.GetHashCode();

        int seed = Math.Abs(hash ^ dateHash);
        return seed;
    }

    public static void SaveChunk(Chunk chunk)
    {
        Directory.CreateDirectory(SaveLocation());
        string saveFile = Path.Combine(SaveLocation(), FileName(chunk.ChunkX, chunk.ChunkZ));

        using var fileStream = new FileStream(saveFile, FileMode.Create);
        using var gzipStream = new GZipStream(fileStream, CompressionLevel.Fastest);
        using var writer = new BinaryWriter(gzipStream);

        writer.Write(chunk.ChunkX);
        writer.Write(chunk.ChunkZ);

        var nonAirBlocks = new List<(ushort index, byte blockType, byte meta)>();
        for (int y = 0; y < Chunk.HEIGHT; y++)
        {
            for (int z = 0; z < Chunk.DEPTH; z++)
            {
                for (int x = 0; x < Chunk.WIDTH; x++)
                {
                    var block = chunk.GetBlock(x, y, z);
                    if (block == BlockType.Air)
                        continue;

                    ushort packedIndex = (ushort)(x + z * Chunk.WIDTH + y * Chunk.WIDTH * Chunk.DEPTH);
                    byte meta = (byte)chunk.GetMetadata(x, y, z);
                    nonAirBlocks.Add((packedIndex, (byte)block, meta));
                }
            }
        }

        writer.Write(nonAirBlocks.Count);
        // High bit of block type byte flags metadata presence
        foreach (var (index, blockType, meta) in nonAirBlocks)
        {
            writer.Write(index);
            if (meta != 0)
            {
                writer.Write((byte)(blockType | 0x80));
                writer.Write(meta);
            }
            else
            {
                writer.Write(blockType);
            }
        }
    }

    public static bool Load(Chunk chunk)
    {
        string saveFile = Path.Combine(SaveLocation(), FileName(chunk.ChunkX, chunk.ChunkZ));

        if (!File.Exists(saveFile))
            return false;

        using var fileStream = new FileStream(saveFile, FileMode.Open);
        using var gzipStream = new GZipStream(fileStream, CompressionMode.Decompress);
        using var reader = new BinaryReader(gzipStream);

        int chunkX = reader.ReadInt32();
        int chunkZ = reader.ReadInt32();
        int blockCount = reader.ReadInt32();

        for (int i = 0; i < blockCount; i++)
        {
            ushort packedIndex = reader.ReadUInt16();
            byte rawBlockType = reader.ReadByte();

            bool hasMeta = (rawBlockType & 0x80) != 0;
            byte blockType = (byte)(rawBlockType & 0x7F);
            byte meta = hasMeta ? reader.ReadByte() : (byte)0;

            int x = packedIndex % Chunk.WIDTH;
            int z = (packedIndex / Chunk.WIDTH) % Chunk.DEPTH;
            int y = packedIndex / (Chunk.WIDTH * Chunk.DEPTH);

            chunk.SetBlock(x, y, z, (BlockType)blockType);
            if (meta != 0)
                chunk.SetMetadata(x, y, z, meta);
        }

        Console.WriteLine($"Loaded chunk at {chunk.ChunkX}, {chunk.ChunkZ} from {SaveLocation()}");
        return true;
    }
    
    public static bool HasSavedChunks(string worldName)
    {
        string worldPath = Path.Combine(SAVE_LOCATION, worldName);
        if (!Directory.Exists(worldPath))
            return false;
        return Directory.GetFiles(worldPath, "chunk_*.bin").Length > 0;
    }

    public static void DeleteWorld(string worldName)
    {
        string worldPath = Path.Combine(SAVE_LOCATION, worldName);
        if (Directory.Exists(worldPath))
        {
            Directory.Delete(worldPath, true);
            Console.WriteLine($"Deleted world: {worldName}");
        }
    }

    public static List<WorldSaveData> GetAllWorlds()
    {
        var worlds = new List<WorldSaveData>();
            
        if (!Directory.Exists(SAVE_LOCATION))
            return worlds;

        var worldDirs = Directory.GetDirectories(SAVE_LOCATION);
            
        foreach (var dir in worldDirs)
        {
            string worldName = Path.GetFileName(dir);
            string oldWorldName = s_WorldName;
            s_WorldName = worldName;
            var worldData = LoadWorldData(worldName);
            s_WorldName = oldWorldName;
            if (worldData != null)
            {
                var data = worldData;

                if (data.LastPlayed == DateTime.MinValue)
                {
                    try
                    {
                        data.LastPlayed = Directory.GetCreationTime(dir);
                    }
                    catch
                    {
                        data.LastPlayed = DateTime.Now.AddDays(-365);
                    }
                }

                worlds.Add(data);
            }
        }

        worlds.Sort((w1, w2) => w2.LastPlayed.CompareTo(w1.LastPlayed));

        return worlds;
    }
}
