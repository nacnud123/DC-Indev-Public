// AABB for collision. Axis-Aligned Bounding Box | DA | 2/5/26


namespace VoxelEngine.GameEntity;

/// <summary>
/// Axis-Aligned Bounding Box - the basic volume used throughout the engine for entity/block collision (see <see cref="Physics"/>) and for entity targeting/raycasting (see <c>Entity.IsLookedAt</c>). Coordinates are in continuous world space (blocks as the unit), not the integer <c>Vector3i</c> block-grid coordinates.
/// </summary>
public struct Aabb
{
    // Min is the box's lower corner (smallest X/Y/Z), Max is the upper corner. All math in this struct assumes Min <= Max on every axis - callers are responsible for keeping that true.
    public Vector3 Min, Max;

    public Aabb(Vector3 min, Vector3 max) { Min = min; Max = max; }

    // 3-Axis overlap test: returns true only if the ranges overlap on X, Y, AND Z simultaneously. Uses strict inequalities, so boxes that merely touch (share a face) do not count as intersecting.
    public bool Intersects(Aabb other) =>
        Min.X < other.Max.X && Max.X > other.Min.X &&
        Min.Y < other.Max.Y && Max.Y > other.Min.Y &&
        Min.Z < other.Max.Z && Max.Z > other.Min.Z;

    // Returns a new AABB shifted by vector v (both Min and Max moved by the same amount).
    public Aabb Offset(Vector3 v) => new(Min + v, Max + v);

    /// <summary>
    /// Extends the AABB in the direction of v, used to create a swept volume that covers both the box's current position and where it will end up after moving by v this tick/frame. Physics uses this to gather every block the entity could possibly touch during its movement, rather than just the blocks under its start or end position (which would miss fast-moving entities passing through blocks - "tunneling").
    /// </summary>
    public Aabb Expand(Vector3 v)
    {
        var min = Min; var max = Max;
        // Only push the box's leading edge on each axis outward - e.g. moving left (negative X) extends Min.X but leaves Max.X alone, since the box isn't growing to the right.
        if (v.X < 0)
            min.X += v.X;
        else
            max.X += v.X;

        if (v.Y < 0)
            min.Y += v.Y;
        else
            max.Y += v.Y;

        if (v.Z < 0)
            min.Z += v.Z;
        else
            max.Z += v.Z;

        return new Aabb(min, max);
    }

    // Builds the unit-cube AABB occupying block grid cell (x, y, z) - i.e. the default full-block collision volume before any per-block bounds (slabs, stairs, etc.) are applied.
    public static Aabb BlockAabb(int x, int y, int z) => new(new Vector3(x, y, z), new Vector3(x + 1, y + 1, z + 1));
}
