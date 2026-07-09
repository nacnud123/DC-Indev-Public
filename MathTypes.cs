namespace VoxelEngine;

/// <summary>
/// Integer-valued 3D vector, used for block/chunk coordinates where a `System.Numerics.Vector3` (float-based) would introduce rounding error or unwanted implicit fractional positions. Implicitly convertible to `Vector3` so it can be passed anywhere a float vector is expected (e.g. shader uniforms, entity positions) without an explicit cast at every call site.
/// </summary>
public struct Vector3i : IEquatable<Vector3i>
{
    public int X, Y, Z;
    public Vector3i(int x, int y, int z) { X = x; Y = y; Z = z; }
    public static Vector3i Zero => new(0, 0, 0);
    public static Vector3i operator +(Vector3i a, Vector3i b) => new(a.X + b.X, a.Y + b.Y, a.Z + b.Z);
    public static Vector3i operator -(Vector3i a, Vector3i b) => new(a.X - b.X, a.Y - b.Y, a.Z - b.Z);
    public static bool operator ==(Vector3i a, Vector3i b) => a.X == b.X && a.Y == b.Y && a.Z == b.Z;
    public static bool operator !=(Vector3i a, Vector3i b) => !(a == b);
    public bool Equals(Vector3i other) => this == other;
    public override bool Equals(object? obj) => obj is Vector3i v && this == v;
    public override int GetHashCode() => HashCode.Combine(X, Y, Z);
    public override string ToString() => $"({X}, {Y}, {Z})";
    public System.Numerics.Vector3 ToVector3() => new(X, Y, Z);
    // Implicit (not explicit) so Vector3i can be passed directly to APIs expecting a float Vector3 (rendering, physics) without callers needing to write `.ToVector3()` everywhere.
    public static implicit operator System.Numerics.Vector3(Vector3i v) => new(v.X, v.Y, v.Z);
}

/// <summary>
/// Integer-valued 2D vector, primarily used for chunk-grid coordinates (chunk X/Z indices) where fractional values would be meaningless. Readonly since chunk coordinates are treated as immutable keys (e.g. dictionary keys, grid indices) rather than mutable state.
/// </summary>
public readonly struct Vector2i : IEquatable<Vector2i>
{
    public readonly int X, Y;
    public Vector2i(int x, int y) { X = x; Y = y; }
    public static Vector2i Zero => new(0, 0);
    public static bool operator ==(Vector2i a, Vector2i b) => a.X == b.X && a.Y == b.Y;
    public static bool operator !=(Vector2i a, Vector2i b) => !(a == b);
    public bool Equals(Vector2i other) => this == other;
    public override bool Equals(object? obj) => obj is Vector2i v && this == v;
    // Combine both components so vectors with swapped X/Y don't collide in hash-based collections.
    public override int GetHashCode() => HashCode.Combine(X, Y);
    public override string ToString() => $"({X}, {Y})";
}
