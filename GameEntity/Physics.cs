// Main class physics file, does physics and collision stuff | DA | 2/5/26

using VoxelEngine.Terrain;
using VoxelEngine.Terrain.Blocks;

namespace VoxelEngine.GameEntity;

/// <summary>
/// Static helper for entity-vs-voxel-world physics: gravity, swept AABB collision resolution (per-axis, X/Z/Y independently), stair-stepping, and ground-contact snapping. All velocities here are in blocks/second unless already converted to a per-tick displacement by the caller (see <c>Entity.Tick</c>, which multiplies velocity by <c>TickSystem.TICK_DURATION</c> before calling <see cref="MoveWithCollision"/> - so the `velocity` parameter below is actually a per-tick displacement in blocks, not a blocks/second rate).
/// </summary>
public static class Physics
{
    // Consts
    public const float GRAVITY = 32f;       // blocks/second^2
    public const float TERMINAL_VEL = 78.4f; // blocks/second, max downward fall speed
    public const float JUMP_VEL = 9f;        // blocks/second, initial upward velocity on jump
    private const float COLLISION_EPSILON = 0.0001f; // small buffer to avoid resting exactly flush with a surface (prevents jitter/tunneling from float precision)

    public const float STEP_HEIGHT = 0.6f; // max block height an entity can auto climb without jumping (e.g. slabs, stairs)

    /// <summary>
    /// Moves `box` by `velocity` (a per-tick displacement, blocks) while resolving collisions against solid world blocks, and returns the actual displacement applied (which may be smaller than requested if something was in the way). Order of operations matters: Y is resolved before X/Z so that gravity/ground contact is established before horizontal sliding is computed against the now-settled box, and optional stair-stepping is attempted only if horizontal movement got blocked.
    /// </summary>
    public static Vector3 MoveWithCollision(World world, Aabb box, Vector3 velocity, float stepHeight = 0f)
    {
        // Build the search area Take the entity AABB, expand it by the entity's velocity to get a bounding box that covers the entire movement path.
        var searchBox = box.Expand(velocity);
        if (stepHeight > 0)
            searchBox = searchBox.Expand(new Vector3(0, stepHeight, 0)); // also cover the step-up probe volume

        // Scans every block position in the search area. If a block is solid, create an AABB using block's min and max bounds.
        var blocks = GetCollidingBlocks(world, searchBox);

        // Done first because vertical collisions are the most important to gravity
        velocity.Y = ResolveAxis(box, blocks, velocity.Y, 1);
        box = box.Offset(new Vector3(0, velocity.Y, 0));

        // Then try moving on the X and Z X is resolved against the box already moved by Y (not the original box), so a corner case where Y movement changes what's blocking X is handled correctly.
        float normalX = ResolveAxis(box, blocks, velocity.X, 0);
        var boxAfterX = box.Offset(new Vector3(normalX, 0, 0));
        // Z is resolved against the box already moved by X, chaining the axis order X -> Z.
        float normalZ = ResolveAxis(boxAfterX, blocks, velocity.Z, 2);

        // If X or Z movement was blocked (didn't move as far as requested), see if stepping up (like climbing a slab or stair) would let the entity get past the obstruction.
        if (stepHeight > 0 && (MathF.Abs(normalX) < MathF.Abs(velocity.X) - COLLISION_EPSILON || MathF.Abs(normalZ) < MathF.Abs(velocity.Z) - COLLISION_EPSILON))
        {
            // Probe: lift the box up by stepHeight, then retry the X/Z movement from up there.
            float up = ResolveAxis(box, blocks, stepHeight, 1);
            var steppedBox = box.Offset(new Vector3(0, up, 0));

            float stepX = ResolveAxis(steppedBox, blocks, velocity.X, 0);
            steppedBox = steppedBox.Offset(new Vector3(stepX, 0, 0));
            float stepZ = ResolveAxis(steppedBox, blocks, velocity.Z, 2);
            steppedBox = steppedBox.Offset(new Vector3(0, 0, stepZ));

            // Then settle back down as far as possible (typically back onto the stepped-up surface).
            float down = ResolveAxis(steppedBox, blocks, -up, 1);

            // Only accept the stepped path if it actually made more horizontal progress than the flat (non-stepped) path - otherwise stepping up would be pointless (e.g. a full wall).
            if (stepX * stepX + stepZ * stepZ > normalX * normalX + normalZ * normalZ)
                return new Vector3(stepX, velocity.Y + up + down, stepZ);
        }

        velocity.X = normalX;
        velocity.Z = normalZ;

        // Step-down: if on ground (stepHeight > 0), moving horizontally, and Y velocity is only gravity (not jumping), try snapping down to maintain ground contact on stairs
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

    // Internal variant that reuses an already-gathered block list (avoids re-querying the world grid when the caller already has candidate blocks, e.g. during step-down resolution above).
    private static bool IsOnGround(World? _ignored, Aabb box, List<Aabb> blocks)
    {
        // Probe a hairline sliver just below the box; if it intersects any solid block, we're grounded.
        Aabb below = box.Offset(new Vector3(0, -0.01f, 0));
        foreach (var block in blocks)
        {
            if (below.Intersects(block))
                return true;
        }
        return false;
    }

    // Public entry point: queries the world directly for a fresh block list under the box.
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
        // axis: 0 = X, 1 = Y, 2 = Z. The switch below picks which two OTHER axes must already overlap for this block to be a candidate obstacle along `axis` (a block only blocks motion along X if the box already overlaps it in Y and Z, etc).
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

            // Extract the relevant axis's min/max from both the moving box and the obstacle.
            float boxMin = axis == 0 ? box.Min.X : axis == 1 ? box.Min.Y : box.Min.Z;
            float boxMax = axis == 0 ? box.Max.X : axis == 1 ? box.Max.Y : box.Max.Z;
            float blkMin = axis == 0 ? block.Min.X : axis == 1 ? block.Min.Y : block.Min.Z;
            float blkMax = axis == 0 ? block.Max.X : axis == 1 ? block.Max.Y : block.Max.Z;

            // Moving in the positive direction and the block is ahead of us: clamp vel so we stop just short of the block's near face (COLLISION_EPSILON keeps us from touching exactly).
            if (vel > 0 && boxMax <= blkMin)
                vel = MathF.Min(vel, MathF.Max(0, blkMin - boxMax - COLLISION_EPSILON));
            // Same idea moving in the negative direction against a block behind/below us.
            else if (vel < 0 && boxMin >= blkMax)
                vel = MathF.Max(vel, MathF.Min(0, blkMax - boxMin + COLLISION_EPSILON));
        }

        return vel;
    }

    // Reused across calls to avoid a per-tick heap allocation for every entity's collision query. NOT thread-safe - assumes physics runs single-threaded on the main/tick thread.
    private static readonly List<Aabb> sBlockBuffer = new(32);

    // Scans every block cell inside `search`'s bounding volume and collects an AABB for every solid block found (special-cased for stairs, which contribute two boxes instead of one).
    private static List<Aabb> GetCollidingBlocks(World world, Aabb search)
    {
        sBlockBuffer.Clear();
        var result = sBlockBuffer;
        // Floor to get inclusive integer block-cell ranges covering the search volume.
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
                            // Non-full blocks (slabs, fences, etc.) use per-block-type bounds instead of the full unit cube.
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

    // Stairs are modeled as two boxes: a full-footprint bottom slab plus a back "riser" half-block whose position depends on which way the stair faces, approximating the stepped stair shape for collision purposes without needing a full non-box collision mesh.
    private static void AddStairCollisionBoxes(List<Aabb> result, int x, int y, int z, BlockType bt)
    {
        // Box 1: bottom slab (full X/Z, bottom half)
        result.Add(new Aabb(new Vector3(x, y, z), new Vector3(x + 1, y + 0.5f, z + 1)));

        // Box 2: back step (half extent based on facing)
        int facing = World.Current?.GetMetadata(x, y, z) ?? 0;

        // facing values 0-3 correspond to the four cardinal directions the stair can be placed facing (matches the metadata convention used elsewhere for directional blocks).
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