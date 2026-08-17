using VoxelEngine.Terrain.Infinite;

namespace VoxelEngine.Terrain;

public partial class World
{
    /// Every block change since the last flush, deduplicated by position. Null until a host asks
    /// for one, so singleplayer pays nothing.
    private Dictionary<Vector3i, (BlockType type, byte metadata)>? mChangeJournal;

    public void BeginJournalling() => mChangeJournal ??= new();

    /// Called from SetBlock/SetBlockDirect/SetMetadata - one hook, so nothing can slip past.
    internal void RecordChange(int x, int y, int z, BlockType type, byte metadata)
    {
        if (mChangeJournal == null)
            return;

        // Last write per position wins: water that flows through three states in one tick sends
        // one packet, not three.
        mChangeJournal[new Vector3i(x, y, z)] = (type, metadata);
    }

    public IEnumerable<IGrouping<ChunkCoord, KeyValuePair<Vector3i, (BlockType type, byte metadata)>>> DrainChanges()
    {
        if (mChangeJournal == null || mChangeJournal.Count == 0)
            yield break;

        var snapshot = mChangeJournal;
        mChangeJournal = new();

        foreach (var group in snapshot.GroupBy(kv => ChunkCoord.FromWorldBlock(kv.Key.X, kv.Key.Z)))
            yield return group;
    }
}