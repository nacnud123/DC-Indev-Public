namespace VoxelEngine.Terrain.Infinite;

public interface IChunkSource
{
    void RequestChunk(ChunkCoord coord);
    bool TryDequeueCompleted(out Chunk chunk);

    /// <summary>
    /// Whether chunks from this source own their save file. False on a multiplayer client: the
    /// server owns that world, so writing its chunks into the local DCIndevSaves folder would
    /// scribble server terrain over whichever singleplayer save was last opened.
    /// </summary>
    bool PersistsToDisk => true;
}