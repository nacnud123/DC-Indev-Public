// Parent AI class holds stuff for path calculation and other movement code | DA | 2/5/26

using VoxelEngine.GameEntity.AI.Pathfinding;
using VoxelEngine.Terrain;

namespace VoxelEngine.GameEntity.AI;

/// <summary>
/// Abstract base class for all mob AI controllers (passive and hostile). Owns a shared A* pathfinder instance and provides common helper logic used by subclasses: path recalculation, arrival checks, ground-level scanning, step-up/jump detection, and body-facing. Subclasses implement <see cref="Tick"/> to drive their own state machine (idle/wander/chase/attack/etc).
/// </summary>
public abstract class EntityAi
{
    // The mob this AI instance is controlling.
    protected readonly Entity ParentEntity;
    // Shared RNG for wander direction/timing decisions.
    protected readonly Random Random = new();
    // One pathfinder per AI instance; AStarPathfinder caches its working sets internally between calls, so each mob gets its own to avoid cross-talk between concurrent searches.
    private readonly AStarPathfinder mPathfinder = new();

    // Current queued path of block-position waypoints, closest waypoint on top (Stack = LIFO).
    protected Stack<Vector3i>? CurrentPath;
    // The block-space destination the current path (if any) was computed towards.
    protected Vector3i? CurrentTarget;
    // Generic countdown timer (in game ticks) used by subclasses to drive their own state machines.
    protected int StateTimer;
    // Ticks remaining before the path to CurrentTarget should be recomputed.
    protected int PathRecalculateTimer;

    // Set by OnHurt() when the entity takes damage; consumed by subclasses to react (e.g. immediately start chasing/aggro) on the next Tick.
    protected bool WasHurt;

    // Overridden by AI types that have a flee state (e.g. low-health passive mobs) so other systems can query whether this mob is currently running away.
    public virtual bool IsFleeing => false;

    protected EntityAi(Entity entity)
    {
        ParentEntity = entity;
    }

    /// <summary>
    /// Runs one game-tick's worth of AI logic. Called every tick from the entity's update loop. Implementations own their own state machine and are responsible for setting ParentEntity.Velocity/Yaw as needed.
    /// </summary>
    public abstract void Tick(World world);

    /// <summary>
    /// Recomputes <see cref="CurrentPath"/> from the entity's current (floored) position to <see cref="CurrentTarget"/> using A*. No-op if no target is set. Expensive relative to other AI work, so callers should gate this behind a cooldown timer rather than calling it every tick.
    /// </summary>
    protected void RecalculatePath(World world)
    {
        if (!CurrentTarget.HasValue)
            return;

        Vector3i startPos = new(
            (int)MathF.Floor(ParentEntity.Position.X),
            (int)MathF.Floor(ParentEntity.Position.Y),
            (int)MathF.Floor(ParentEntity.Position.Z)
        );

        CurrentPath = mPathfinder.FindPath(world, startPos, CurrentTarget.Value);
    }

    /// <summary>
    /// True once the entity is horizontally within 0.25 blocks of the center of CurrentTarget on both axes (Y is intentionally ignored - only XZ proximity matters for "have we arrived" purposes). Also true if there is no target at all.
    /// </summary>
    protected bool ReachedTarget()
    {
        if (!CurrentTarget.HasValue)
            return true;

        // +0.5f targets the horizontal center of the target block rather than its corner.
        float distX = MathF.Abs(ParentEntity.Position.X - (CurrentTarget.Value.X + 0.5f));
        float distZ = MathF.Abs(ParentEntity.Position.Z - (CurrentTarget.Value.Z + 0.5f));

        return distX < 0.25f && distZ < 0.25f;
    }

    /// <summary>
    /// Scans vertically at the given (x, z) column for the nearest walkable Y level (solid block below, two air blocks above for feet+head clearance) to the entity's current height. Searches downward first from the entity's current Y, then a short distance upward (up to 4 blocks) if nothing was found below. Returns -1 if no walkable ground is found in range.
    /// </summary>
    protected int FindGroundLevel(World world, float x, float z)
    {
        int startY = (int)MathF.Floor(ParentEntity.Position.Y);
        int xI = (int)MathF.Floor(x);
        int zI = (int)MathF.Floor(z);

        // Downward: walk toward y=0 looking for the first "solid floor, 2 air blocks above" spot.
        for (int y = startY; y > 0; y--)
        {
            if (world.GetBlock(xI, y - 1, zI) != BlockType.Air && world.GetBlock(xI, y, zI) == BlockType.Air && world.GetBlock(xI, y + 1, zI) == BlockType.Air)
            {
                return y;
            }
        }

        // Upward: only check a small window (5 blocks) above the entity - covers stepping onto nearby raised terrain without scanning the whole column.
        for (int y = startY + 1; y < startY + 5; y++)
        {
            if (world.GetBlock(xI, y - 1, zI) != BlockType.Air && world.GetBlock(xI, y, zI) == BlockType.Air && world.GetBlock(xI, y + 1, zI) == BlockType.Air)
            {
                return y;
            }
        }

        return -1;
    }

    /// <summary>
    /// True if moving one block in the given horizontal direction (dirX, dirZ) would run the entity into a solid block at foot height with clear space above it (i.e. a single-block step the entity should jump over rather than walk into).
    /// </summary>
    protected bool ShouldJump(World world, float dirX, float dirZ)
    {
        // Sample exactly 1 block ahead in the movement direction.
        int checkX = (int)MathF.Floor(ParentEntity.Position.X + dirX * 1.0f);
        int checkY = (int)MathF.Floor(ParentEntity.Position.Y);
        int checkZ = (int)MathF.Floor(ParentEntity.Position.Z + dirZ * 1.0f);

        bool blockAtFeet = world.GetBlock(checkX, checkY, checkZ) != BlockType.Air;
        bool blockAtHead = world.GetBlock(checkX, checkY + 1, checkZ) != BlockType.Air;

        // Only jump for a single-block-high obstacle; a wall (solid at head height too) can't be jumped.
        return blockAtFeet && !blockAtHead;
    }

    // Rotates the entity's yaw to face its current horizontal velocity direction. The -PI/2 offset accounts for the model's forward axis not being aligned with +Z.
    protected void FaceMovementDirection()
    {
        if (MathF.Abs(ParentEntity.Velocity.X) > 0.01f || MathF.Abs(ParentEntity.Velocity.Z) > 0.01f)
            ParentEntity.Yaw = MathF.Atan2(ParentEntity.Velocity.X, ParentEntity.Velocity.Z) - MathF.PI / 2;
    }

    /// <summary>
    /// Called by the owning entity when it takes damage. Sets a flag consumed on the next Tick so subclasses can react (e.g. instantly aggro/chase the attacker).
    /// </summary>
    public void OnHurt()
    {
        WasHurt = true;
    }
}
