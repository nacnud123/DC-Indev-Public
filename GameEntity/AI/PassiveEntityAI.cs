// Main file for passive AI, they kind of just roam around | DA | 2/5/26
using OpenTK.Mathematics;
using VoxelEngine.Core;
using VoxelEngine.Terrain;

namespace VoxelEngine.GameEntity.AI;

public class PassiveEntityAi : EntityAi
{
    private enum State
    {
        Idle,
        Wandering,
        Fleeing
    }

    private const int IDLE_TICKS = 60;
    private const int WANDER_TICKS = 160;
    private const int FLEE_TICKS = 100;
    private const int PATH_RECALC_TICKS = 20;
    private const float FLEE_DISTANCE = 10f;
    private const int WANDER_RADIUS = 10;

    private State mCurrentState = State.Idle;
    private int mFleeTimer;


    public PassiveEntityAi(Entity entity) : base(entity)
    {
    }

    public override void Tick(World world)
    {
        UpdateTimers();
        UpdateState(world);
        ExecuteState(world);
        FaceMovementDirection();
    }

    private void UpdateTimers()
    {
        StateTimer--;
        mFleeTimer--;
    }

    private void UpdateState(World world)
    {
        if (WasHurt)
        {
            mCurrentState = State.Fleeing;
            mFleeTimer = FLEE_TICKS;
            WasHurt = false;
            CurrentPath = null;
            return;
        }

        if (mCurrentState == State.Fleeing && mFleeTimer <= 0)
        {
            mCurrentState = State.Idle;
            StateTimer = IDLE_TICKS;
            CurrentPath = null;
            return;
        }

        switch (mCurrentState)
        {
            case State.Idle:
                if (StateTimer <= 0)
                {
                    mCurrentState = State.Wandering;
                    StateTimer = WANDER_TICKS;
                    PickRandomWanderTarget(world);
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

            case State.Fleeing:
                FleeFromPlayer(world);
                break;
        }
    }

    private void PickRandomWanderTarget(World world)
    {
        for (int attempts = 0; attempts < 10; attempts++)
        {
            float randomX = ParentEntity.Position.X + Random.Next(-WANDER_RADIUS, WANDER_RADIUS);
            float randomZ = ParentEntity.Position.Z + Random.Next(-WANDER_RADIUS, WANDER_RADIUS);
            int targetY = FindGroundLevel(world, randomX, randomZ);

            if (targetY != -1)
            {
                CurrentTarget = new Vector3i((int)randomX, targetY, (int)randomZ);
                RecalculatePath(world);

                if (CurrentPath != null && CurrentPath.Count > 0)
                    return;
            }
        }

        mCurrentState = State.Idle;
    }

    private void MoveTowardTarget(World world, float speed)
    {
        PathRecalculateTimer--;
        if (PathRecalculateTimer <= 0)
        {
            PathRecalculateTimer = PATH_RECALC_TICKS;
            RecalculatePath(world);
        }

        if (!CurrentTarget.HasValue)
            return;

        float dirX = CurrentTarget.Value.X + 0.5f - ParentEntity.Position.X;
        float dirZ = CurrentTarget.Value.Z + 0.5f - ParentEntity.Position.Z;
        float distance = MathF.Sqrt(dirX * dirX + dirZ * dirZ);

        if (distance < 0.1f)
            return;

        dirX /= distance;
        dirZ /= distance;

        float velY = ParentEntity.Velocity.Y;
        if (ParentEntity.IsOnGround && ShouldJump(world, dirX, dirZ))
            velY = Physics.JUMP_VEL;

        ParentEntity.Velocity = new Vector3(dirX * speed, velY, dirZ * speed);
    }

    private void FleeFromPlayer(World world)
    {
        Vector3 playerPos = Game.Instance.GetPlayer.Position;

        float dirX = ParentEntity.Position.X - playerPos.X;
        float dirZ = ParentEntity.Position.Z - playerPos.Z;
        float distance = MathF.Sqrt(dirX * dirX + dirZ * dirZ);

        if (distance > 0.1f)
        {
            dirX /= distance;
            dirZ /= distance;
        }
        else
        {
            float angle = Random.NextSingle() * MathF.PI * 2f;
            dirX = MathF.Cos(angle);
            dirZ = MathF.Sin(angle);
        }

        float fleeSpeed = ParentEntity.WalkSpeed * 1.5f;
        float velY = ParentEntity.Velocity.Y;

        if (ParentEntity.IsOnGround && ShouldJump(world, dirX, dirZ))
            velY = Physics.JUMP_VEL;

        ParentEntity.Velocity = new Vector3(dirX * fleeSpeed, velY, dirZ * fleeSpeed);
    }
}
