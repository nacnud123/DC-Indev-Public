// Saves and loads worlds. Saves the worlds / chunks similarly to how structures are saved and loaded. Also helps give the title screen data like a sorted list of all the worlds | DA | 2/21/26
using System.IO.Compression;
using System.Xml.Serialization;
using VoxelEngine.Core;
using VoxelEngine.Terrain;

namespace VoxelEngine.Saving;

/// <summary>Simple (X, Y, Z, BlockType) tuple used when passing individual blocks around save/load code; not itself serialized to disk.</summary>
public struct SaveBlock
{
    public int X, Y, Z;
    public BlockType Block;
}

/// <summary>
/// Central hub for all on-disk persistence: per-chunk binary files (gzip-compressed, sparse - only non-air blocks are stored) and per-world XML metadata (seed, size, player state, etc). Also provides the world browser (main menu) with a sorted list of saved worlds. Every world lives under <c>DCIndevSaves/&lt;WorldName&gt;/</c>.
/// </summary>
public class Serialization
{
    // Name of the world currently being saved/loaded; set once when entering a world.
    public static string WorldName { get; set; } = "Testing";

    public const string SAVE_LOCATION = "DCIndevSaves";

    /// <summary>Directory path for the currently active world, creating it on disk if it doesn't already exist.</summary>
    public static string SaveLocation() => SaveLocation(WorldName);

    /// <summary>Directory path for an arbitrary named world, creating it on disk if it doesn't already exist.</summary>
    private static string SaveLocation(string worldName)
    {
        string saveLocation = SAVE_LOCATION + "/" + worldName + "/";

        if (!Directory.Exists(saveLocation))
        {
            Directory.CreateDirectory(saveLocation);
        }

        return saveLocation;
    }

    public static string FileName(int x, int z) => $"chunk_{x}_{z}.bin";
    public static string GetWorldDataFile() => "world_info.xml";

    /// <summary>Loads a world's XML metadata file (seed, size, player state, inventory, etc), or null if the world hasn't been saved yet.</summary>
    public static WorldSaveData? LoadWorldData(string worldName)
    {
        string metaDataFilePath = Path.Combine(SaveLocation(worldName), GetWorldDataFile());

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

    /// <summary>Overwrites the active world's <c>world_info.xml</c> with the given metadata snapshot.</summary>
    public static void SaveWorldMetadata(WorldSaveData saveData) =>
        SaveWorldMetadata(WorldName, saveData);

    private static void SaveWorldMetadata(string worldName, WorldSaveData saveData)
    {
        string metadataPath = Path.Combine(SaveLocation(worldName), GetWorldDataFile());

        var serializer = new XmlSerializer(typeof(WorldSaveData));
        using var stream = new FileStream(metadataPath, FileMode.Create);
        serializer.Serialize(stream, saveData);
    }

    /// <summary>Bumps a world's "last played" timestamp to now and rewrites its metadata file, used by the world browser for sort order.</summary>
    public static void UpdateLastPlayed(string worldName)
    {
        var worldData = LoadWorldData(worldName);
        if (worldData != null)
        {
            worldData.LastPlayed = DateTime.Now;
            SaveWorldMetadata(worldName, worldData);
        }
    }

    /// <summary>Reads just the chunk-grid size (in chunks) out of a world's metadata, without loading the whole world.</summary>
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

    /// <summary>Creates a brand-new world's metadata (generating an ID and, unless overridden, a deterministic seed from the name+date), writes it to disk, and returns it.</summary>
    public static WorldSaveData CreateWorld(string worldName, int? customSeed = null, int worldSize = 8, int worldType = 0, int worldTheme = 0, float worldTime = .1f, bool isCreative = false)
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
            WorldTime = worldTime,
            IsCreative = isCreative
        };

        SaveWorldMetadata(worldData);
        return worldData;
    }

    /// <summary>Generates a pseudo-unique positive integer ID for a new world from a fresh GUID's hash code.</summary>
    private static int GenerateWorldId()
    {
        return Math.Abs(Guid.NewGuid().GetHashCode());
    }

    /// <summary>Derives a deterministic default seed from the world name XOR'd with today's date, so re-creating a world with the same name on the same day reproduces the same terrain (unless a custom seed is supplied).</summary>
    private static int GenSeed(string worldName)
    {
        int hash = worldName.GetHashCode();

        var today = DateTime.Today;
        int dateHash = today.GetHashCode();

        int seed = Math.Abs(hash ^ dateHash);
        return seed;
    }

    /// <summary>
    /// Writes a chunk to <c>chunk_&lt;x&gt;_&lt;z&gt;.bin</c> in the active world's save folder using a sparse, gzip-compressed binary format: chunk coordinates, a count of non-air blocks, then for each one a packed 16-bit position index plus a block-type byte (with its high bit repurposed as a "has metadata" flag) and an optional metadata byte. Air blocks are never written since most of a chunk's volume is air - this keeps save files small. Overwrites any existing file for this chunk.
    /// </summary>
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

                    // Pack (x, y, z) into a single 16-bit index; must match the unpacking math in Load().
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
                // Block types must stay under 128 (0x80) for this bit-stealing trick to be unambiguous.
                writer.Write((byte)(blockType | 0x80));
                writer.Write(meta);
            }
            else
            {
                writer.Write(blockType);
            }
        }
    }

    /// <summary>
    /// Reads a chunk's saved binary file (see <see cref="SaveChunk"/> for the format) and populates <paramref name="chunk"/> in place. Returns false without modifying the chunk if no save file exists yet (e.g. a chunk that has never been player-modified and can just be regenerated).
    /// </summary>
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

            // Undo the high-bit "has metadata" flag stashed in the block type byte during save.
            bool hasMeta = (rawBlockType & 0x80) != 0;
            byte blockType = (byte)(rawBlockType & 0x7F);
            byte meta = hasMeta ? reader.ReadByte() : (byte)0;

            // Unpack the 16-bit index back into (x, y, z); inverse of the packing in SaveChunk.
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
    
    /// <summary>Quick existence check used to distinguish a brand-new world (needs full terrain gen) from one with existing player-modified chunks on disk.</summary>
    public static bool HasSavedChunks(string worldName)
    {
        string worldPath = Path.Combine(SAVE_LOCATION, worldName);
        if (!Directory.Exists(worldPath))
            return false;
        return Directory.GetFiles(worldPath, "chunk_*.bin").Length > 0;
    }

    /// <summary>Permanently deletes a world's entire save directory (chunks, metadata, block entities) from disk.</summary>
    public static void DeleteWorld(string worldName)
    {
        string worldPath = Path.Combine(SAVE_LOCATION, worldName);
        if (Directory.Exists(worldPath))
        {
            Directory.Delete(worldPath, true);
            Console.WriteLine($"Deleted world: {worldName}");
        }
    }

    /// <summary>
    /// Scans the save root for every world subdirectory with valid metadata and returns them sorted most-recently-played first, for display in the world selection/main menu screen. Worlds with a missing/zeroed last-played timestamp fall back to the directory's creation time (or a year-old placeholder if that can't be read), so they still sort sensibly rather than floating to the top.
    /// </summary>
    public static List<WorldSaveData> GetAllWorlds()
    {
        var worlds = new List<WorldSaveData>();
            
        if (!Directory.Exists(SAVE_LOCATION))
            return worlds;

        var worldDirs = Directory.GetDirectories(SAVE_LOCATION);
            
        foreach (var dir in worldDirs)
        {
            string worldName = Path.GetFileName(dir);
            var worldData = LoadWorldData(worldName);
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
