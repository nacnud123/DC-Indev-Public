// Raw, thread-safe block writes for background generation and chunk loading. | DA

namespace VoxelEngine.Terrain;

// Third partial declaration of Chunk. Private fields are visible - partial parts are one class.
public partial class Chunk
{
    /// <summary>
    /// Light emitters found during off-thread lighting seeding, in chunk-local coordinates, waiting
    /// to be pushed into the shared BFS queue on the main thread. Null once committed (or if
    /// lighting was done on the main thread to begin with).
    /// </summary>
    internal List<Vector3i>? PendingLightEmitters;

    /// <summary>
    /// Voxels on the edge of this chunk's lit region, in chunk-local coordinates, waiting to be
    /// pushed into the sky-light BFS on the main thread. The column scan only sends light straight
    /// down; these are where it has to start travelling sideways to reach under overhangs.
    /// </summary>
    internal List<Vector3i>? PendingSkyLightSeeds;

    /// <summary>
    /// Raw block write for background generation/loading.
    ///
    /// The normal <c>SetBlock</c> is unsafe off the main thread: it calls <c>MarkDirty()</c>, which
    /// calls <c>World.NotifyDirty(this)</c>, which adds to a bare <c>HashSet&lt;Chunk&gt;</c>. Two
    /// generation workers hitting that concurrently corrupts it. This writes only into this chunk's
    /// own byte array, so it's safe from any thread as long as the chunk isn't yet visible to the
    /// rest of the game.
    ///
    /// Skipping MarkDirty costs nothing: a freshly constructed Chunk already starts dirty, so it
    /// gets meshed once when the streamer integrates it.
    /// </summary>
    public void SetBlockGenerating(int x, int y, int z, BlockType type)
    {
        if (x < 0 || x >= WIDTH || y < 0 || y >= HEIGHT || z < 0 || z >= DEPTH)
            return;

        int index = GetIndex(x, y, z);
        TrackSpongeChange((BlockType)mBlocks[index], type);
        mBlocks[index] = (byte)type;
    }

    /// <summary>
    /// Raw metadata write, same threading contract as <see cref="SetBlockGenerating"/>.
    /// Metadata is nibble-packed two blocks per byte, so this is a read-modify-write of the
    /// containing byte - which is exactly why it must not race with another writer.
    /// </summary>
    public void SetMetadataGenerating(int x, int y, int z, byte value)
    {
        if (x < 0 || x >= WIDTH || y < 0 || y >= HEIGHT || z < 0 || z >= DEPTH)
            return;

        int index = GetIndex(x, y, z), byteIndex = index / 2;
        if ((index & 1) == 0) mMetadata[byteIndex] = (byte)((mMetadata[byteIndex] & 0xF0) | (value & 0x0F));
        else                  mMetadata[byteIndex] = (byte)((mMetadata[byteIndex] & 0x0F) | ((value & 0x0F) << 4));
    }
}
