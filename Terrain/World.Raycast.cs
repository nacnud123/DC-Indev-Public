// A partial class that extends World. Has functions related to raycasting. | DA | 2/14/26
using OpenTK.Mathematics;
using VoxelEngine.GameEntity;
using VoxelEngine.Terrain.Blocks;

namespace VoxelEngine.Terrain;

public partial class World
{
    public RaycastHit Raycast(Vector3 origin, Vector3 direction, float maxDist = 8f)
    {
        var blockHit = RaycastBlocks(origin, direction, maxDist);
        var entityHit = RaycastEntitiesInternal(origin, direction, maxDist);

        return entityHit.Distance < blockHit.Distance ? entityHit : blockHit;
    }

    private RaycastHit RaycastEntitiesInternal(Vector3 origin, Vector3 direction, float maxDistance)
    {
        var result = RaycastHit.Miss;

        foreach (var entity in mEntities)
        {
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

    private RaycastHit RaycastBlocks(Vector3 origin, Vector3 direction, float maxDist)
    {
        Vector3 dir = direction.Normalized();
        Vector3i current = new((int)MathF.Floor(origin.X), (int)MathF.Floor(origin.Y), (int)MathF.Floor(origin.Z));
        Vector3i step = new(dir.X >= 0 ? 1 : -1, dir.Y >= 0 ? 1 : -1, dir.Z >= 0 ? 1 : -1);

        Vector3 tDelta = new(
            dir.X != 0 ? MathF.Abs(1f / dir.X) : float.MaxValue,
            dir.Y != 0 ? MathF.Abs(1f / dir.Y) : float.MaxValue,
            dir.Z != 0 ? MathF.Abs(1f / dir.Z) : float.MaxValue
        );

        Vector3 tMax = new(
            dir.X != 0 ? (dir.X > 0 ? current.X + 1 - origin.X : origin.X - current.X) * tDelta.X : float.MaxValue,
            dir.Y != 0 ? (dir.Y > 0 ? current.Y + 1 - origin.Y : origin.Y - current.Y) * tDelta.Y : float.MaxValue,
            dir.Z != 0 ? (dir.Z > 0 ? current.Z + 1 - origin.Z : origin.Z - current.Z) * tDelta.Z : float.MaxValue
        );

        float dist = 0;
        Vector3i? prev = null;

        while (dist < maxDist)
        {
            var block = GetBlock(current.X, current.Y, current.Z);
            if (block != BlockType.Air && block != BlockType.Water)
            {
                var pos = new Vector3(current.X, current.Y, current.Z);
                var min = BlockRegistry.GetBoundsMin(block) + pos;
                var max = BlockRegistry.GetBoundsMax(block) + pos;

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

    private static bool SlabIntersect(float origin, float dir, float min, float max, ref float tmin, ref float tmax)
    {
        if (MathF.Abs(dir) < 1e-6f)
            return origin >= min && origin <= max;

        float t1 = (min - origin) / dir;
        float t2 = (max - origin) / dir;

        if (t1 > t2) (t1, t2) = (t2, t1);

        tmin = MathF.Max(tmin, t1);
        tmax = MathF.Min(tmax, t2);

        return tmin <= tmax;
    }
}
