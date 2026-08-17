// One implementation of the chunk binary format, shared by disk and (later) the wire. | DA

using System.IO.Compression;

using VoxelEngine.Terrain;

namespace VoxelEngine.Saving;

/// <summary>
/// The chunk payload format, in exactly one place.
///
/// <c>Serialization.SaveChunk</c> and <c>Serialization.Load</c> each had this packing loop inlined
/// and written out by hand. Two more call sites are coming (send a chunk, receive a chunk), and
/// four hand-rolled copies of a bit-packed format is how you end up with chunks that save fine but
/// arrive corrupted. Extract it once.
///
/// Format: <c>[int32 count]</c>, then per non-air block
/// <c>[uint16 packedIndex][byte type | 0x80 if metadata follows][byte metadata?]</c>.
/// Air is never stored - it's most of the volume.
///
/// ⚠ Stealing the high bit of the type byte caps block ids at 127. That's fine today and is a
/// problem the moment the block count grows; the build guide's Stage 0 notes cover the fix
/// (variants in metadata rather than new enum values).
/// </summary>
public static class ChunkBlob
{
    public static void Write(BinaryWriter w, Chunk chunk)
    {
        var nonAir = new List<(ushort index, byte type, byte meta)>();

        // Iteration order is y/z/x to match the packing below; it is also the order the existing
        // save files were written in, so this stays byte-compatible with worlds already on disk.
        for (int y = 0; y < Chunk.HEIGHT; y++)
        for (int z = 0; z < Chunk.DEPTH;  z++)
        for (int x = 0; x < Chunk.WIDTH;  x++)
        {
            var block = chunk.GetBlock(x, y, z);
            if (block == BlockType.Air)
                continue;

            ushort packed = (ushort)(x + z * Chunk.WIDTH + y * Chunk.WIDTH * Chunk.DEPTH);
            nonAir.Add((packed, (byte)block, (byte)chunk.GetMetadata(x, y, z)));
        }

        w.Write(nonAir.Count);
        foreach (var (index, type, meta) in nonAir)
        {
            w.Write(index);
            if (meta != 0) { w.Write((byte)(type | 0x80)); w.Write(meta); }
            else           { w.Write(type); }
        }
    }

    /// <summary>
    /// Reads a blob into a chunk through the THREAD-SAFE raw setters, so this is usable from a
    /// worker thread. <c>Serialization.Load</c> is not - it goes through <c>Chunk.SetBlock</c>,
    /// which reaches into shared World state.
    /// </summary>
    public static void Read(BinaryReader r, Chunk chunk)
    {
        int count = r.ReadInt32();
        for (int i = 0; i < count; i++)
        {
            ushort packed = r.ReadUInt16();
            byte raw = r.ReadByte();
            bool hasMeta = (raw & 0x80) != 0;
            byte type = (byte)(raw & 0x7F);
            byte meta = hasMeta ? r.ReadByte() : (byte)0;

            // Inverse of the packing in Write.
            int x = packed % Chunk.WIDTH;
            int z = (packed / Chunk.WIDTH) % Chunk.DEPTH;
            int y = packed / (Chunk.WIDTH * Chunk.DEPTH);

            chunk.SetBlockGenerating(x, y, z, (BlockType)type);
            if (meta != 0)
                chunk.SetMetadataGenerating(x, y, z, meta);
        }
    }

    /// <summary>
    /// Worker-thread-safe disk load. Returns false if the chunk has never been saved.
    /// </summary>
    public static bool TryLoadFromDisk(Chunk chunk, string saveDir)
    {
        string path = Path.Combine(saveDir, Serialization.FileName(chunk.ChunkX, chunk.ChunkZ));
        if (!File.Exists(path))
            return false;

        using var file = new FileStream(path, FileMode.Open);
        using var gzip = new GZipStream(file, CompressionMode.Decompress);
        using var r = new BinaryReader(gzip);

        r.ReadInt32(); r.ReadInt32();      // chunk X/Z header - already known from the filename
        Read(r, chunk);
        return true;
    }

    /// <summary>
    /// Encodes a chunk as a gzipped blob - the payload of the Map Chunk packet.
    /// </summary>
    /// <remarks>
    /// Real Beta sent deflate-compressed block bytes + metadata/blocklight/skylight nibbles:
    /// 81,920 bytes uncompressed for a full column. This sparse blob is smaller and simpler, and
    /// costs nothing here because the client recomputes its own lighting on arrival. Only switch to
    /// the Beta layout if a real Beta client ever needs to connect.
    /// </remarks>
    public static byte[] ToWireBytes(Chunk chunk)
    {
        using var raw = new MemoryStream();
        using (var gzip = new GZipStream(raw, CompressionLevel.Fastest, leaveOpen: true))
        using (var w = new BinaryWriter(gzip))
            Write(w, chunk);

        return raw.ToArray();
    }

    public static void FromWireBytes(Chunk chunk, byte[] gzipped)
    {
        using var mem = new MemoryStream(gzipped);
        using var gzip = new GZipStream(mem, CompressionMode.Decompress);
        using var r = new BinaryReader(gzip);

        Read(r, chunk);
    }
}
