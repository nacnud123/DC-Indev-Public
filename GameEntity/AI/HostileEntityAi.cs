// Base hostile AI. Detects player, chases via A*, wanders when no LOS. | DA | 3/2/26

using OpenTK.Mathematics;
using VoxelEngine.Core;
using VoxelEngine.Terrain;

namespace VoxelEngine.GameEntity.AI;

public class HostileEntityAi : EntityAi
{
    private enum State
    {
        Idle,
        Chasing,
        Wandering
    }

    private const int PATH_RECALC_TICKS = 20;
    private const int WANDER_RADIUS = 10;

    protected virtual float DetectionRange => 32f;

    // Subclasses override AttackRange to set the range at which OnAttackEntity is called.
    protected virtual float AttackRange => 2.5f;

    private State mCurrentState = State.Idle;
    private bool mSuppressMovement;

    private float mWanderDirX;
    private float mWanderDirZ;
    private int mWanderTimer;

    public HostileEntityAi(Entity entity) : base(entity)
    {
    }

    public override void Tick(World world)
    {
        // OnHurt immediately start chasing
        if (WasHurt)
        {
            WasHurt = false;
            mCurrentState = State.Chasing;
            CurrentPath = null;
        }

        mSuppressMovement = false;

        Vector3 playerPos = Game.Instance.GetPlayer.Position;
        float dist = (playerPos - ParentEntity.Position).Length;

        bool inRange = dist <= DetectionRange;
        bool hasLos = inRange && HasLineOfSight(world, playerPos);

        if (hasLos)
        {
            mCurrentState = State.Chasing;
        }
        else if (mCurrentState == State.Chasing)
        {
            mCurrentState = State.Wandering;
            CurrentPath = null;
        }

        switch (mCurrentState)
        {
            case State.Chasing:
                FacePlayer(playerPos);
                if (hasLos && dist <= AttackRange)
                {
                    // In attack range with clear LOS
                    ParentEntity.Velocity = new Vector3(0f, ParentEntity.Velocity.Y, 0f);
                    OnAttackEntity(world, dist);
                    mSuppressMovement = true;
                }
                else
                {
                    ChasePlayer(world, playerPos);
                }

                break;

            case State.Wandering:
                Wander(world);
                break;

            case State.Idle:
                ParentEntity.Velocity = new Vector3(0f, ParentEntity.Velocity.Y, 0f);
                
                if (hasLos)
                    mCurrentState = State.Chasing;
                break;
        }

        if (!mSuppressMovement)
            FaceMovementDirection();
    }

    // Called each tick the entity is within AttackRange with LOS.
    protected virtual void OnAttackEntity(World world, float dist)
    {
    }

    private void FacePlayer(Vector3 playerPos)
    {
        float dx = playerPos.X - ParentEntity.Position.X;
        float dz = playerPos.Z - ParentEntity.Position.Z;
        float hLen = MathF.Sqrt(dx * dx + dz * dz);
        if (hLen > 0.01f)
            ParentEntity.Yaw = MathF.Atan2(dx, dz) - MathF.PI / 2f;
    }

    private void ChasePlayer(World world, Vector3 playerPos)
    {
        // Keep path pointed at player, recalculate every N ticks
        CurrentTarget = new Vector3i(
            (int)MathF.Floor(playerPos.X),
            (int)MathF.Floor(playerPos.Y),
            (int)MathF.Floor(playerPos.Z));

        PathRecalculateTimer--;
        if (PathRecalculateTimer <= 0 || CurrentPath == null || CurrentPath.Count == 0)
        {
            PathRecalculateTimer = PATH_RECALC_TICKS;
            RecalculatePath(world);
        }

        MoveAlongPath(world);
    }

    private void MoveAlongPath(World world)
    {
        if (CurrentPath == null || CurrentPath.Count == 0)
        {
            ParentEntity.Velocity = new Vector3(0f, ParentEntity.Velocity.Y, 0f);
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
        if (ParentEntity.IsOnGround && (waypoint.Y > currentY || ShouldJump(world, dx, dz)))
            velY = Physics.JUMP_VEL;

        ParentEntity.Velocity = new Vector3(dx * ParentEntity.WalkSpeed, velY, dz * ParentEntity.WalkSpeed);
    }

    private void Wander(World world)
    {
        mWanderTimer--;
        if (mWanderTimer <= 0)
        {
            mWanderTimer = 60 + Random.Next(60);
            if (Random.NextDouble() < 0.4)
            {
                mWanderDirX = 0f;
                mWanderDirZ = 0f;
            }
            else
            {
                float angle = (float)(Random.NextDouble() * MathF.PI * 2f);
                mWanderDirX = MathF.Cos(angle);
                mWanderDirZ = MathF.Sin(angle);
            }
        }

        if (mWanderDirX == 0f && mWanderDirZ == 0f)
        {
            ParentEntity.Velocity = new Vector3(0f, ParentEntity.Velocity.Y, 0f);
            return;
        }

        float velY = ParentEntity.Velocity.Y;
        if (ParentEntity.IsOnGround && ShouldJump(world, mWanderDirX, mWanderDirZ))
            velY = Physics.JUMP_VEL;

        ParentEntity.Velocity = new Vector3(
            mWanderDirX * ParentEntity.WalkSpeed * 0.5f,
            velY,
            mWanderDirZ * ParentEntity.WalkSpeed * 0.5f);
    }

    private bool HasLineOfSight(World world, Vector3 playerPos)
    {
        Vector3 eye = ParentEntity.Position + new Vector3(0f, ParentEntity.Height * 0.9f, 0f);
        Vector3 delta = playerPos + new Vector3(0f, 0.9f, 0f) - eye;
        float dist = delta.Length;
        if (dist < 0.01f) return true;
        var hit = world.RaycastBlocksOnly(eye, delta / dist, dist, solidOnly: true);
        return hit.Type != RaycastHitType.Block;
    }
}