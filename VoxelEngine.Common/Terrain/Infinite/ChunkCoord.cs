namespace VoxelEngine.Terrain.Infinite;

// Struct, not class: this is the key for every GetBlock/GetSkyLight/GetMetadata lookup, so a class
// would heap-allocate a throwaway key hundreds of thousands of times per chunk meshed.
public readonly struct ChunkCoord : IEquatable<ChunkCoord>
{
    public readonly int X, Z;

    public ChunkCoord(int x, int z)
    {
        this.X = x;
        this.Z = z;
    }

    // >> 4 floors toward negative infinity; `/ 16` truncates and would put blocks -15..-1 in chunk 0.
    public static ChunkCoord FromWorldBlock(int worldX, int worldZ) => new(worldX >> 4, worldZ >> 4);

    public bool Equals(ChunkCoord other) => X == other.X && Z == other.Z;
    public override bool Equals(object? o) => o is ChunkCoord c && Equals(c);
    public override int GetHashCode() => X * 73856093 ^ Z * 19349663;
    public override string ToString() => $"({X}, {Z})";
}

public readonly struct ChunkObserver
{
    public readonly int Id;
    public readonly Vector3 Position;
    public readonly int ViewRadius;

    public ChunkObserver(int id, Vector3 position, int viewRadius)
    {
        this.Id = id;
        this.Position = position;
        this.ViewRadius = viewRadius;
    }
}

public static class ChunkMath
{
    public static ChunkCoord ToChunkCoord(Vector3 pos) =>
        ChunkCoord.FromWorldBlock((int)MathF.Floor(pos.X), (int)MathF.Floor(pos.Z));

    public static IEnumerable<ChunkCoord> CircleAround(ChunkCoord center, int radius)
    {
        for (int dx = -radius; dx <= radius; dx++)
        {
            for (int dz = -radius; dz <= radius; dz++)
            {
                if (dx * dx + dz * dz <= radius * radius)
                {
                    yield return new ChunkCoord(center.X + dx, center.Z + dz);
                }
            }
        }
    }

    public static float Distance(ChunkCoord coord, Vector3 worldPos)
    {
        var other = ToChunkCoord(worldPos);
        int dx = coord.X - other.X, dz = coord.Z - other.Z;
        return MathF.Sqrt(dx * dx + dz * dz);
    }

    public static float NearestObserverDistance(ChunkCoord coord, IReadOnlyList<ChunkObserver> observers)
    {
        float best = float.MaxValue;
        foreach (var o in observers)
        {
            best = MathF.Min(best, Distance(coord, o.Position));
        }

        return best;
    }

    public static bool WantedByAny(ChunkCoord coord, IReadOnlyList<ChunkObserver> observers, int slack)
    {
        foreach (var o in observers)
        {
            if (Distance(coord, o.Position) <= o.ViewRadius + slack)
                return true;
        }

        return false;
    }

    public static IEnumerable<ChunkCoord> Within(Vector3 pos, int radiusChunks) =>
        CircleAround(ToChunkCoord(pos), radiusChunks);
}