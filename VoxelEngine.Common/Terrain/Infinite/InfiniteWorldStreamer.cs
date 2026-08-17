using VoxelEngine.Saving;

namespace VoxelEngine.Terrain.Infinite;

public sealed class InfiniteWorldStreamer : IDisposable
{
    private const int UNLOAD_SLACK = 3; // Don't unload as you step back
    private const int MAX_REQUESTS_PER_UPDATE = 4;
    // Integration is cheap now that it neither lights nor meshes - it just files the chunk and
    // queues it. The real per-frame ceiling is World's mesh rebuild budget.
    private const int MAX_INTEGRATIONS_PER_UPDATE = 4;
    private const int MAX_UNLOADS_PER_UPDATE = 4; //Disk cost
    private const int SIMULATION_DISTANCE = 6;

    private readonly World mWorld;
    private readonly IChunkSource mSource;
    private readonly Dictionary<ChunkCoord, Chunk> mChunks = new();

    public InfiniteWorldStreamer(World world, IChunkSource source)
    {
        mWorld = world;
        mSource = source;
    }

    /// <summary>
    /// Raised on the main thread the moment a chunk becomes resident. The network path needs it:
    /// a BlockChange can arrive before the chunk it belongs to, and has to be replayed once it lands.
    /// </summary>
    public Action<ChunkCoord>? ChunkIntegrated;

    public IReadOnlyCollection<Chunk> LoadedChunks => mChunks.Values;

    /// <summary>How many chunks are resident right now - the number to watch for the Stage 2 checkpoint.</summary>
    public int LoadedCount => mChunks.Count;

    public Chunk? GetChunk(int cx, int cz) => mChunks.TryGetValue(new ChunkCoord(cx, cz), out var c) ? c : null;

    public void Update(IReadOnlyList<ChunkObserver> observers)
    {
        IntegrateReady(observers);
        RequestMissing(observers);
        UnloadUnwanted(observers);
        UpdateSimulationFlags(observers);
    }

    private void RequestMissing(IReadOnlyList<ChunkObserver> observers)
    {
        int started = 0;

        // Nearest-first across all observers, so the world fills in around every player at once rather than finishing one player's ring before starting another's
        foreach (var coord in Desired(observers).OrderBy(c => ChunkMath.NearestObserverDistance(c, observers)))
        {
            if (started >= MAX_REQUESTS_PER_UPDATE)
                break;

            if (mChunks.ContainsKey(coord))
                continue;

            mSource.RequestChunk(coord);
            started++;
        }
    }

    private HashSet<ChunkCoord> Desired(IReadOnlyList<ChunkObserver> observers)
    {
        var set = new HashSet<ChunkCoord>();
        foreach (var o in observers)
        {
            foreach (var c in ChunkMath.CircleAround(ChunkMath.ToChunkCoord(o.Position), o.ViewRadius))
            {
                set.Add(c);
            }
        }

        return set;
    }

    // Main thread: GL upload and shared-World mutation happen only here.
    private void IntegrateReady(IReadOnlyList<ChunkObserver> observers)
    {
        int done = 0;
        while (done < MAX_INTEGRATIONS_PER_UPDATE && mSource.TryDequeueCompleted(out var chunk))
        {
            var coord = new ChunkCoord(chunk.ChunkX, chunk.ChunkZ);

            // The player may have walked away while this generated. Drop it; if still wanted, RequestMissing asks again next frame
            if (!ChunkMath.WantedByAny(coord, observers, UNLOAD_SLACK))
                continue;

            if (mChunks.ContainsKey(coord))
                continue;

            mChunks[coord] = chunk;

            // Cheap now: the two full-volume passes already ran on the generation worker, so this
            // only seeds the shared light queue.
            mWorld.CalculateInitialLightingFor(chunk);

            // Queue the mesh instead of building it here. Building inline meant every integration
            // cost a full mesh on top of World's per-frame rebuild budget, so the budget wasn't
            // actually bounding anything. NotifyDirty rather than MarkDirty because a fresh chunk
            // is already flagged dirty internally, which makes MarkDirty a no-op.
            mWorld.NotifyDirty(chunk);

            // Neighbours were meshed while this chunk was absent. Meaning their border faces are stale
            MarkNeighborDirty(coord.X - 1, coord.Z, chunk);
            MarkNeighborDirty(coord.X + 1, coord.Z, chunk);
            MarkNeighborDirty(coord.X, coord.Z - 1, chunk);
            MarkNeighborDirty(coord.X, coord.Z + 1, chunk);

            ChunkIntegrated?.Invoke(coord);

            done++;
        }
    }

    private void MarkNeighborDirty(int cx, int cz) => MarkNeighborDirty(cx, cz, null);

    private void MarkNeighborDirty(int cx, int cz, Chunk? arrival)
    {
        if (!mChunks.TryGetValue(new ChunkCoord(cx, cz), out var n))
            return;

        n.MarkDirty();

        // Both chunks' edge light was computed while the other didn't exist, so neither ever flowed
        // across. Now that both are here, seed the difference.
        if (arrival != null)
            mWorld.SeedLightAcrossBorder(arrival, n);
    }

    private void UnloadUnwanted(IReadOnlyList<ChunkObserver> observers)
    {
        int unloaded = 0;
        List<ChunkCoord>? doomed = null;

        foreach (var (coord, _) in mChunks)
        {
            if (unloaded >= MAX_UNLOADS_PER_UPDATE)
                break;

            if (ChunkMath.WantedByAny(coord, observers, UNLOAD_SLACK))
                continue;

            (doomed ??= new()).Add(coord);
            unloaded++;
        }

        if (doomed == null)
            return;

        foreach (var coord in doomed)
        {
            var chunk = mChunks[coord];
            if (mSource.PersistsToDisk && chunk.HasChunkBeenModified)
                Serialization.SaveChunk(chunk);

            chunk.IsLoaded = false;
            chunk.Dispose();
            mChunks.Remove(coord);
        }
    }

    // Simulation follows players, not the camera
    private void UpdateSimulationFlags(IReadOnlyList<ChunkObserver> observers)
    {
        foreach (var (coord, chunk) in mChunks)
        {
            chunk.IsLoaded = ChunkMath.NearestObserverDistance(coord, observers) <= SIMULATION_DISTANCE;
        }
    }

    // Server told us to drop this one (Stage 6, Pre-Chunk with load=false).
    public void ForceUnload(ChunkCoord coord)
    {
        if (!mChunks.Remove(coord, out var chunk))
            return;

        chunk.IsLoaded = false;
        chunk.Dispose();
    }

    public void Dispose()
    {
        foreach (var c in mChunks.Values)
        {
            if (mSource.PersistsToDisk && c.HasChunkBeenModified)
                Serialization.SaveChunk(c);

            // Dispose unconditionally. Nesting this inside the save check leaks the GPU buffers of
            // every chunk you only ever looked at - which, on a world you walked across, is most of
            // them.
            c.Dispose();
        }

        mChunks.Clear();
    }
}