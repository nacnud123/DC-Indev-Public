// Main class for passive mob AI. The AI wanders toward grass/lit areas, resumes wandering after being hit | DA | 3/8/26
using OpenTK.Mathematics;
using VoxelEngine.Terrain;

namespace VoxelEngine.GameEntity.AI;

public class PassiveEntityAi : EntityAi
{
    private enum State { Idle, Wandering }

    private const int IDLE_TICKS = 60;
    private const int WANDER_TICKS = 160;
    private const int PATH_RECALC_TICKS = 20;
    private const int WANDER_RADIUS = 10;
    private const int WANDER_Y_RANGE = 4;
    private const int WANDER_SAMPLES = 200;
    private const int RANDOM_REWANDER_CHANCE = 100;
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

    // Samples 200 random positions and picks the highest-scored reachable one. Grass below scores +10; otherwise scores by light level (prefers bright areas).
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

    // +10 for grass underfoot; otherwise score by ambient light level.
    private float ScoreWanderPosition(World world, int x, int y, int z)
    {
        if (world.GetBlock(x, y - 1, z) == BlockType.Grass)
            return 10f;

        int light = Math.Max(world.GetSkyLight(x, y, z), world.GetBlockLight(x, y, z));
        return light / 15f - 0.5f;
    }

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
            if (IsInFluid(world) && Random.NextSingle() < FLUID_JUMP_CHANCE)
                velY = Physics.JUMP_VEL;
            else if (waypoint.Y > currentY || ShouldJump(world, dx, dz))
                velY = Physics.JUMP_VEL;
        }

        ParentEntity.Velocity = new Vector3(dx * speed, velY, dz * speed);
    }

    private bool IsInFluid(World world)
    {
        int x = (int)MathF.Floor(ParentEntity.Position.X);
        int y = (int)MathF.Floor(ParentEntity.Position.Y);
        int z = (int)MathF.Floor(ParentEntity.Position.Z);
        var block = world.GetBlock(x, y, z);
        return block == BlockType.Water || block == BlockType.Lava;
    }
}
