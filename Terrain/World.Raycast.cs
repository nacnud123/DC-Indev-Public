// A partial class that extends World. Has functions related to raycasting. | DA | 2/14/26

using VoxelEngine.GameEntity;
using VoxelEngine.Terrain.Blocks;

namespace VoxelEngine.Terrain;

public partial class World
{
    // Extra padding added to a torch's selection AABB so thin torch geometry is easier to click on (2 texels out of a 16-texel block, i.e. 1/8 of a block on each side).
    private const float TORCH_SELECT_PADDING = 2f / 16f;

    /// <summary>
    /// Casts a ray from origin in direction and returns whichever of "closest block" or "closest entity" is hit first. Used for the player's crosshair (block breaking/placing) and for mob/attack targeting. See RaycastBlocks for the block-stepping algorithm and RaycastEntitiesInternal for entity hit-testing.
    /// </summary>
    public RaycastHit Raycast(Vector3 origin, Vector3 direction, float maxDist = 8f, bool solidOnly = false)
    {
        var blockHit  = RaycastBlocks(origin, direction, maxDist, solidOnly);
        var entityHit = RaycastEntitiesInternal(origin, direction, maxDist);

        return entityHit.Distance < blockHit.Distance ? entityHit : blockHit;
    }

    // Block-only raycast — ignores entities. Used for LOS checks so other mobs don't count as cover.
    public RaycastHit RaycastBlocksOnly(Vector3 origin, Vector3 direction, float maxDist, bool solidOnly = false)
    {
        return RaycastBlocks(origin, direction, maxDist, solidOnly);
    }

    // Linear scan over every entity in the world checking if the ray passes close enough to be considered "looked at" (delegated to Entity.IsLookedAt, which presumably tests against the entity's own bounding box/radius). Keeps the closest one found within maxDistance.
    private RaycastHit RaycastEntitiesInternal(Vector3 origin, Vector3 direction, float maxDistance)
    {
        var result = RaycastHit.Miss;

        foreach (var entity in mEntities)
        {
            if (!entity.IsTargetable)
                continue;

            if (entity.IsLookedAt(origin, direction, maxDistance, out float dist) && dist < result.Distance)
            {
                result = new RaycastHit
                {
                    Type = RaycastHitType.Entity,
                    Distance = dist,
                    Entity = entity
                };
            }
        }

        return result;
    }

    // Voxel raycasting via the Amanatides & Woo DDA (grid traversal) algorithm: instead of sampling the ray at fixed intervals (which can skip thin blocks or double-check the same voxel), we step exactly one voxel boundary at a time, always advancing into whichever neighboring cell (X, Y, or Z) the ray would cross next. - "current" is the voxel the ray is currently inside (integer block coords). - "step" is +1/-1 per axis depending on the ray's direction along that axis. - "tDelta" is how far (in ray-parameter t, i.e. world units since dir is normalized) the ray must travel along each axis to cross one full voxel width. - "tMax" is the ray-parameter t at which the ray next crosses a voxel boundary on each axis, initialized to the distance from the origin to the first boundary crossing. Each loop iteration advances along whichever axis has the smallest tMax (i.e. the next boundary the ray will hit), and grows that axis's tMax by tDelta for the following step. This visits every voxel the ray actually passes through, in order, with no gaps or repeats.
    private RaycastHit RaycastBlocks(Vector3 origin, Vector3 direction, float maxDist, bool solidOnly = false)
    {
        Vector3 dir = Vector3.Normalize(direction);
        Vector3i current = new((int)MathF.Floor(origin.X), (int)MathF.Floor(origin.Y), (int)MathF.Floor(origin.Z));
        Vector3i step = new(dir.X >= 0 ? 1 : -1, dir.Y >= 0 ? 1 : -1, dir.Z >= 0 ? 1 : -1);

        // Distance along the ray needed to move one full voxel step on each axis. Axes the ray isn't moving along (dir component == 0) get MaxValue so they never "win" the min-tMax comparison below and the ray never steps along them.
        Vector3 tDelta = new(
            dir.X != 0 ? MathF.Abs(1f / dir.X) : float.MaxValue,
            dir.Y != 0 ? MathF.Abs(1f / dir.Y) : float.MaxValue,
            dir.Z != 0 ? MathF.Abs(1f / dir.Z) : float.MaxValue
        );

        // Distance along the ray to the first voxel boundary crossing on each axis, depending on which direction the ray is heading (toward the +1 boundary or the current cell's origin).
        Vector3 tMax = new(
            dir.X != 0 ? (dir.X > 0 ? current.X + 1 - origin.X : origin.X - current.X) * tDelta.X : float.MaxValue,
            dir.Y != 0 ? (dir.Y > 0 ? current.Y + 1 - origin.Y : origin.Y - current.Y) * tDelta.Y : float.MaxValue,
            dir.Z != 0 ? (dir.Z > 0 ? current.Z + 1 - origin.Z : origin.Z - current.Z) * tDelta.Z : float.MaxValue
        );

        float dist = 0;
        // Tracks the previous voxel visited, so if we hit a solid block we can report the empty cell right before it as the "place block here" position (RaycastHit.PlacePos).
        Vector3i? prev = null;

        while (dist < maxDist)
        {
            var block = GetBlock(current.X, current.Y, current.Z);
            if (block != BlockType.Air && !BlockRegistry.IsFluid(block) && (!solidOnly || BlockRegistry.IsSolid(block)))
            {
                var pos = new Vector3(current.X, current.Y, current.Z);

                var minLocal = BlockRegistry.GetBoundsMin(block);
                var maxLocal = BlockRegistry.GetBoundsMax(block);

                // Make torches easier to target: expand selection AABB a bit (raycast only).
                if (block == BlockType.Torch)
                {
                    int meta = GetMetadata(current.X, current.Y, current.Z);
                    if (meta > 0)
                        (minLocal, maxLocal) = BlockTorch.GetWallTorchBounds(meta - 1);

                    ExpandAndClampLocalAabb(ref minLocal, ref maxLocal, TORCH_SELECT_PADDING);
                }

                var min = minLocal + pos;
                var max = maxLocal + pos;

                if (RayIntersectsAabb(origin, dir, min, max, out float hitDist) && hitDist <= maxDist)
                {
                    return new RaycastHit
                    {
                        Type = RaycastHitType.Block,
                        Distance = hitDist,
                        BlockPos = current,
                        PlacePos = prev,
                        BlockType = block
                    };
                }
            }

            prev = current;

            // Step into whichever neighboring voxel the ray crosses into next (the axis with the smallest tMax is the next boundary the ray reaches); advance dist to that crossing point and push that axis's tMax out by one more voxel width for the next iteration.
            if (tMax.X < tMax.Y && tMax.X < tMax.Z)
            {
                current.X += step.X;
                dist = tMax.X;
                tMax.X += tDelta.X;
            }
            else if (tMax.Y < tMax.Z)
            {
                current.Y += step.Y;
                dist = tMax.Y;
                tMax.Y += tDelta.Y;
            }
            else
            {
                current.Z += step.Z;
                dist = tMax.Z;
                tMax.Z += tDelta.Z;
            }
        }

        return RaycastHit.Miss;
    }

    // Grows a block's local-space (0..1 per axis) selection box by pad on every side, then clamps back into the [0,1] unit cube so the expansion never spills into a neighboring block's space.
    private static void ExpandAndClampLocalAabb(ref Vector3 min, ref Vector3 max, float pad)
    {
        min -= new Vector3(pad);
        max += new Vector3(pad);

        min.X = Math.Clamp(min.X, 0f, 1f);
        min.Y = Math.Clamp(min.Y, 0f, 1f);
        min.Z = Math.Clamp(min.Z, 0f, 1f);

        max.X = Math.Clamp(max.X, 0f, 1f);
        max.Y = Math.Clamp(max.Y, 0f, 1f);
        max.Z = Math.Clamp(max.Z, 0f, 1f);
    }

    // Standard slab-method ray/AABB intersection test: intersect the ray against the box's three pairs of parallel planes (slabs) and check whether the overlap of all three per-axis intervals is non-empty. hitDist is the entry distance along the ray if it hits.
    private static bool RayIntersectsAabb(Vector3 origin, Vector3 dir, Vector3 min, Vector3 max, out float hitDist)
    {
        float tmin = 0, tmax = float.MaxValue;
        hitDist = float.MaxValue;

        if (!SlabIntersect(origin.X, dir.X, min.X, max.X, ref tmin, ref tmax)) 
            return false;

        if (!SlabIntersect(origin.Y, dir.Y, min.Y, max.Y, ref tmin, ref tmax)) 
            return false;

        if (!SlabIntersect(origin.Z, dir.Z, min.Z, max.Z, ref tmin, ref tmax)) 
            return false;

        hitDist = tmin;
        return true;
    }

    // Intersects the ray against a single axis's [min,max] slab, narrowing the running [tmin,tmax] intersection interval. Returns false as soon as the interval becomes empty (ray misses the box entirely on this axis, or is parallel to the slab and outside it).
    private static bool SlabIntersect(float origin, float dir, float min, float max, ref float tmin, ref float tmax)
    {
        // Ray is (near) parallel to this slab - it never crosses a boundary on this axis, so the only way it can be "inside" is if the origin itself already lies within [min, max].
        if (MathF.Abs(dir) < 1e-6f)
            return origin >= min && origin <= max;

        float t1 = (min - origin) / dir;
        float t2 = (max - origin) / dir;

        // Ensure t1 is the near intersection and t2 the far one regardless of ray direction.
        if (t1 > t2) (t1, t2) = (t2, t1);

        tmin = MathF.Max(tmin, t1);
        tmax = MathF.Min(tmax, t2);

        return tmin <= tmax;
    }
}