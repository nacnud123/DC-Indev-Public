// Main class physics file, does physics and collision stuff | DA | 2/5/26
using OpenTK.Mathematics;
using VoxelEngine.Terrain;
using VoxelEngine.Terrain.Blocks;

namespace VoxelEngine.GameEntity;
public static class Physics
{
    // Consts
    public const float GRAVITY = 32f;
    public const float TERMINAL_VEL = 78.4f;
    public const float JUMP_VEL = 9f;
    private const float COLLISION_EPSILON = 0.0001f;

    public const float STEP_HEIGHT = 0.6f;
    
    // Resolves collisions using a swept axis resolution
    public static Vector3 MoveWithCollision(World world, Aabb box, Vector3 velocity, float stepHeight = 0f)
    {
        // Build the search area
        // Take the entity AABB, expand it by the entity's velocity to get a bounding box that covers the entire movement path.
        var searchBox = box.Expand(velocity);
        if (stepHeight > 0)
            searchBox = searchBox.Expand(new Vector3(0, stepHeight, 0));
        
        // Scans every block position in the search area. If a block is solid, create an AABB using block's min and max bounds.
        var blocks = GetCollidingBlocks(world, searchBox);

        // Done first because vertical collisions are the most important to gravity
        velocity.Y = ResolveAxis(box, blocks, velocity.Y, 1);
        box = box.Offset(new Vector3(0, velocity.Y, 0));

        // Then try moving on the X and Z
        float normalX = ResolveAxis(box, blocks, velocity.X, 0);
        var boxAfterX = box.Offset(new Vector3(normalX, 0, 0));
        float normalZ = ResolveAxis(boxAfterX, blocks, velocity.Z, 2);

        // If X or Z movement was blocked see if you can step up a block
        if (stepHeight > 0 && (MathF.Abs(normalX) < MathF.Abs(velocity.X) - COLLISION_EPSILON || MathF.Abs(normalZ) < MathF.Abs(velocity.Z) - COLLISION_EPSILON))
        {
            float up = ResolveAxis(box, blocks, stepHeight, 1);
            var steppedBox = box.Offset(new Vector3(0, up, 0));

            float stepX = ResolveAxis(steppedBox, blocks, velocity.X, 0);
            steppedBox = steppedBox.Offset(new Vector3(stepX, 0, 0));
            float stepZ = ResolveAxis(steppedBox, blocks, velocity.Z, 2);
            steppedBox = steppedBox.Offset(new Vector3(0, 0, stepZ));

            float down = ResolveAxis(steppedBox, blocks, -up, 1);

            if (stepX * stepX + stepZ * stepZ > normalX * normalX + normalZ * normalZ)
                return new Vector3(stepX, velocity.Y + up + down, stepZ);
        }

        velocity.X = normalX;
        velocity.Z = normalZ;

        // Step-down: if on ground (stepHeight > 0), moving horizontally, and Y velocity is
        // only gravity (not jumping), try snapping down to maintain ground contact on stairs
        if (stepHeight > 0 && velocity.Y <= 0 &&
            (MathF.Abs(velocity.X) > COLLISION_EPSILON || MathF.Abs(velocity.Z) > COLLISION_EPSILON))
        {
            var afterMove = box.Offset(velocity);
            if (!IsOnGround(null!, afterMove, blocks))
            {
                float down = ResolveAxis(afterMove, blocks, -stepHeight, 1);
                if (MathF.Abs(down) > COLLISION_EPSILON && MathF.Abs(down) < stepHeight)
                {
                    // Verify we actually land on something
                    var downBox = afterMove.Offset(new Vector3(0, down, 0));
                    if (IsOnGround(null!, downBox, blocks))
                        velocity.Y += down;
                }
            }
        }

        return velocity;
    }

    private static bool IsOnGround(World? _ignored, Aabb box, List<Aabb> blocks)
    {
        Aabb below = box.Offset(new Vector3(0, -0.01f, 0));
        foreach (var block in blocks)
        {
            if (below.Intersects(block))
                return true;
        }
        return false;
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

    // For each block in the collision list, check if the entity overlaps the block on the other two axes, if so clamp the velocity. Subtract COLLISION_EPSILON to prevent resting exactly on the surface
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
                    var bt = world.GetBlock(x, y, z);
                    if (BlockRegistry.IsSolid(bt))
                    {
                        if (BlockRegistry.GetRenderType(bt) == RenderingType.Stair)
                        {
                            AddStairCollisionBoxes(result, x, y, z, bt);
                        }
                        else
                        {
                            var bMin = BlockRegistry.GetBoundsMin(bt);
                            var bMax = BlockRegistry.GetBoundsMax(bt);
                            result.Add(new Aabb(new Vector3(x, y, z) + bMin, new Vector3(x, y, z) + bMax));
                        }
                    }
                }
            }
        }

        return result;
    }

    private static void AddStairCollisionBoxes(List<Aabb> result, int x, int y, int z, BlockType bt)
    {
        // Box 1: bottom slab (full X/Z, bottom half)
        result.Add(new Aabb(new Vector3(x, y, z), new Vector3(x + 1, y + 0.5f, z + 1)));

        // Box 2: back step (half extent based on facing)
        int facing = World.Current?.GetMetadata(x, y, z) ?? 0;

        Aabb backStep = facing switch
        {
            0 => new Aabb(new Vector3(x, y + 0.5f, z), new Vector3(x + 1, y + 1, z + 0.5f)),
            1 => new Aabb(new Vector3(x, y + 0.5f, z + 0.5f), new Vector3(x + 1, y + 1, z + 1)),
            2 => new Aabb(new Vector3(x + 0.5f, y + 0.5f, z), new Vector3(x + 1, y + 1, z + 1)),
            3 => new Aabb(new Vector3(x, y + 0.5f, z), new Vector3(x + 0.5f, y + 1, z + 1)),
            _ => new Aabb(new Vector3(x, y + 0.5f, z + 0.5f), new Vector3(x + 1, y + 1, z + 1))
        };

        result.Add(backStep);
    }
}