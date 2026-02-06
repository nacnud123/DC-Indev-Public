// Main class physics file, does physics and collision stuff that I don't really understand | DA | 2/5/26
using OpenTK.Mathematics;
using VoxelEngine.Terrain;
using VoxelEngine.Terrain.Blocks;

namespace VoxelEngine.GameEntity;
public static class Physics
{
    public const float GRAVITY = 32f;
    public const float TERMINAL_VEL = 78.4f;
    public const float JUMP_VEL = 9f;
    private const float COLLISION_EPSILON = 0.0001f;

    public static Vector3 MoveWithCollision(World world, Aabb box, Vector3 velocity)
    {
        var blocks = GetCollidingBlocks(world, box.Expand(velocity));

        velocity.Y = ResolveAxis(box, blocks, velocity.Y, 1);
        box = box.Offset(new Vector3(0, velocity.Y, 0));

        velocity.X = ResolveAxis(box, blocks, velocity.X, 0);
        box = box.Offset(new Vector3(velocity.X, 0, 0));

        velocity.Z = ResolveAxis(box, blocks, velocity.Z, 2);

        return velocity;
    }

    public static bool IsOnGround(World world, Aabb box)
    {
        Aabb below = box.Offset(new Vector3(0, -0.01f, 0));

        foreach (var block in GetCollidingBlocks(world, below))
        {
            if (below.Intersects(block))
                return true;
        }

        return false;
    }

    private static float ResolveAxis(Aabb box, List<Aabb> blocks, float vel, int axis)
    {
        foreach (var block in blocks)
        {
            // Check overlap on other axes
            bool overlaps = axis switch
            {
                0 => box.Min.Y < block.Max.Y && box.Max.Y > block.Min.Y && box.Min.Z < block.Max.Z && box.Max.Z > block.Min.Z,
                1 => box.Min.X < block.Max.X && box.Max.X > block.Min.X && box.Min.Z < block.Max.Z && box.Max.Z > block.Min.Z,
                2 => box.Min.X < block.Max.X && box.Max.X > block.Min.X && box.Min.Y < block.Max.Y && box.Max.Y > block.Min.Y,
                _ => false
            };

            if (!overlaps)
                continue;

            float boxMin = axis == 0 ? box.Min.X : axis == 1 ? box.Min.Y : box.Min.Z;
            float boxMax = axis == 0 ? box.Max.X : axis == 1 ? box.Max.Y : box.Max.Z;
            float blkMin = axis == 0 ? block.Min.X : axis == 1 ? block.Min.Y : block.Min.Z;
            float blkMax = axis == 0 ? block.Max.X : axis == 1 ? block.Max.Y : block.Max.Z;

            if (vel > 0 && boxMax <= blkMin)
                vel = MathF.Min(vel, MathF.Max(0, blkMin - boxMax - COLLISION_EPSILON));
            else if (vel < 0 && boxMin >= blkMax)
                vel = MathF.Max(vel, MathF.Min(0, blkMax - boxMin + COLLISION_EPSILON));
        }

        return vel;
    }

    private static List<Aabb> GetCollidingBlocks(World world, Aabb search)
    {
        var result = new List<Aabb>();
        int minX = (int)MathF.Floor(search.Min.X), maxX = (int)MathF.Floor(search.Max.X);
        int minY = (int)MathF.Floor(search.Min.Y), maxY = (int)MathF.Floor(search.Max.Y);
        int minZ = (int)MathF.Floor(search.Min.Z), maxZ = (int)MathF.Floor(search.Max.Z);

        for (int x = minX; x <= maxX; x++)
        {
            for (int y = minY; y <= maxY; y++)
            {
                for (int z = minZ; z <= maxZ; z++)
                {
                    if (BlockRegistry.IsSolid(world.GetBlock(x, y, z)))
                        result.Add(Aabb.BlockAabb(x, y, z));
                }
            }
        }

        return result;
    }
}