// Main class for passive mob AI. The AI wanders toward grass/lit areas, resumes wandering after being hit | DA | 3/8/26

using VoxelEngine.Terrain;

namespace VoxelEngine.GameEntity.AI;

/// <summary>
/// AI for passive mobs (pigs, sheep, etc). Alternates between standing idle and wandering toward a randomly sampled nearby destination, preferring bright/grassy spots. Does not detect or react to the player at all (that's HostileEntityAi's job) - the only player-independent trigger for state changes is elapsed time.
/// </summary>
public class PassiveEntityAi : EntityAi
{
    private enum State { Idle, Wandering }

    // Ticks to remain idle after finishing/abandoning a wander (60 ticks = ~3s).
    private const int IDLE_TICKS = 60;
    // Max ticks to spend wandering toward a target before giving up regardless of arrival (~8s).
    private const int WANDER_TICKS = 160;
    // Ticks between path recalculations while wandering.
    private const int PATH_RECALC_TICKS = 20;
    // Max horizontal blocks from the mob's current position a wander target may be sampled from.
    private const int WANDER_RADIUS = 10;
    // Max vertical difference (blocks) allowed between a sampled target and current position.
    private const int WANDER_Y_RANGE = 4;
    // Number of random candidate positions tried per PickWanderTarget call.
    private const int WANDER_SAMPLES = 200;
    // 1-in-N chance per move tick to abandon the current target and pick a new one early.
    private const int RANDOM_REWANDER_CHANCE = 100;
    // Chance per tick, while on ground and in fluid, to hop (simulates mobs bobbing/swimming out).
    private const float FLUID_JUMP_CHANCE = 0.8f;

    private State mCurrentState = State.Idle;

    public PassiveEntityAi(Entity entity) : base(entity) { }

    public override void Tick(World world)
    {
        StateTimer--;
        UpdateState(world);
        ExecuteState(world);
        FaceMovementDirection();
    }

    // Handles Idle <-> Wandering transitions based on StateTimer expiry, target arrival, or wander timeout.
    private void UpdateState(World world)
    {
        switch (mCurrentState)
        {
            case State.Idle:
                if (StateTimer <= 0)
                {
                    mCurrentState = State.Wandering;
                    StateTimer = WANDER_TICKS;
                    PickWanderTarget(world);
                }
                break;

            case State.Wandering:
                if (StateTimer <= 0 || ReachedTarget())
                {
                    mCurrentState = State.Idle;
                    StateTimer = IDLE_TICKS;
                    CurrentPath = null;
                }
                break;
        }
    }

    // Applies the movement/velocity effects of the current state each tick.
    private void ExecuteState(World world)
    {
        switch (mCurrentState)
        {
            case State.Idle:
                ParentEntity.Velocity = new Vector3(0, ParentEntity.Velocity.Y, 0);
                break;

            case State.Wandering:
                MoveTowardTarget(world, ParentEntity.WalkSpeed);
                break;
        }
    }

    // Samples WANDER_SAMPLES random positions within WANDER_RADIUS/WANDER_Y_RANGE and picks the highest-scored reachable one via ScoreWanderPosition (grass beats light level). Falls back to Idle if no valid ground position was found or no path could be computed.
    private void PickWanderTarget(World world)
    {
        int ox = (int)MathF.Floor(ParentEntity.Position.X);
        int oy = (int)MathF.Floor(ParentEntity.Position.Y);
        int oz = (int)MathF.Floor(ParentEntity.Position.Z);

        Vector3i bestTarget = default;
        float bestScore = float.MinValue;
        bool found = false;

        for (int i = 0; i < WANDER_SAMPLES; i++)
        {
            int rx = ox + Random.Next(-WANDER_RADIUS, WANDER_RADIUS + 1);
            int rz = oz + Random.Next(-WANDER_RADIUS, WANDER_RADIUS + 1);
            int ry = FindGroundLevel(world, rx, rz);

            // Reject unwalkable columns or targets requiring too steep a vertical change.
            if (ry == -1 || MathF.Abs(ry - oy) > WANDER_Y_RANGE)
                continue;

            float score = ScoreWanderPosition(world, rx, ry, rz);
            if (score > bestScore)
            {
                bestScore = score;
                bestTarget = new Vector3i(rx, ry, rz);
                found = true;
            }
        }

        if (!found)
        {
            mCurrentState = State.Idle;
            return;
        }

        CurrentTarget = bestTarget;
        RecalculatePath(world);

        if (CurrentPath == null || CurrentPath.Count == 0)
            mCurrentState = State.Idle;
    }

    // +10 for grass underfoot (strongly preferred, dominates the light-based score); otherwise scores in roughly [-0.5, 0.5] based on max(sky, block) light level out of 15 (Chunk.MAX_LIGHT), so brighter spots are mildly preferred over dark ones.
    private float ScoreWanderPosition(World world, int x, int y, int z)
    {
        if (world.GetBlock(x, y - 1, z) == BlockType.Grass)
            return 10f;

        int light = Math.Max(world.GetSkyLight(x, y, z), world.GetBlockLight(x, y, z));
        return light / 15f - 0.5f;
    }

    // Steps the mob along CurrentPath toward CurrentTarget, occasionally abandoning the path early (RANDOM_REWANDER_CHANCE) to make wandering look less mechanical.
    private void MoveTowardTarget(World world, float speed)
    {
        if (Random.Next(RANDOM_REWANDER_CHANCE) == 0)
        {
            PickWanderTarget(world);
            return;
        }

        PathRecalculateTimer--;
        if (PathRecalculateTimer <= 0)
        {
            PathRecalculateTimer = PATH_RECALC_TICKS;
            RecalculatePath(world);
        }

        if (CurrentPath == null || CurrentPath.Count == 0)
        {
            ParentEntity.Velocity = new Vector3(0, ParentEntity.Velocity.Y, 0);
            return;
        }

        Vector3i waypoint = CurrentPath.Peek();

        float dx = waypoint.X + 0.5f - ParentEntity.Position.X;
        float dz = waypoint.Z + 0.5f - ParentEntity.Position.Z;
        float dist = MathF.Sqrt(dx * dx + dz * dz);

        if (dist < 0.5f)
        {
            // Close enough - consume this waypoint and aim at the next one.
            CurrentPath.Pop();
            if (CurrentPath.Count == 0)
                return;

            waypoint = CurrentPath.Peek();
            dx = waypoint.X + 0.5f - ParentEntity.Position.X;
            dz = waypoint.Z + 0.5f - ParentEntity.Position.Z;
            dist = MathF.Sqrt(dx * dx + dz * dz);
        }

        if (dist < 0.01f)
            return;

        dx /= dist;
        dz /= dist;

        float velY = ParentEntity.Velocity.Y;
        int currentY = (int)MathF.Floor(ParentEntity.Position.Y);

        if (ParentEntity.IsOnGround)
        {
            // In water/lava, mobs periodically hop rather than deterministically jumping, to look like bobbing/swimming instead of a rigid step.
            if (IsInFluid(world) && Random.NextSingle() < FLUID_JUMP_CHANCE)
                velY = Physics.JUMP_VEL;
            else if (waypoint.Y > currentY || ShouldJump(world, dx, dz))
                velY = Physics.JUMP_VEL;
        }

        ParentEntity.Velocity = new Vector3(dx * speed, velY, dz * speed);
    }

    // True if the mob's feet are currently inside a water or lava block.
    private bool IsInFluid(World world)
    {
        int x = (int)MathF.Floor(ParentEntity.Position.X);
        int y = (int)MathF.Floor(ParentEntity.Position.Y);
        int z = (int)MathF.Floor(ParentEntity.Position.Z);
        var block = world.GetBlock(x, y, z);
        return block == BlockType.Water || block == BlockType.Lava;
    }
}
