// Base hostile AI. Detects player, chases via A*, wanders when no LOS. | DA | 3/2/26


using VoxelEngine.Core;
using VoxelEngine.Terrain;

namespace VoxelEngine.GameEntity.AI;

/// <summary>
/// Base AI for hostile mobs (zombies, skeletons, spiders, stalkers, etc). Drives an Idle -> Chasing -> Wandering state machine: the mob wanders aimlessly until it gets line-of-sight on the player within DetectionRange, at which point it paths toward and attacks the player; once LOS is lost it falls back to wandering instead of returning straight to Idle (so it keeps roaming near the player's last known area rather than immediately going idle in place). Subclasses customize behaviour by overriding DetectionRange/AttackRange and OnAttackEntity (called every tick the mob is within attack range with LOS).
/// </summary>
public class HostileEntityAi : EntityAi
{
    private enum State
    {
        Idle,
        Chasing,
        Wandering
    }

    // Ticks between path-to-player recalculations while chasing (20 ticks = ~1s at 20 TPS).
    private const int PATH_RECALC_TICKS = 20;
    // Max blocks from the wander origin the mob will roam while in State.Wandering.
    private const int WANDER_RADIUS = 10;

    // How far (in blocks) the mob can detect the player, subject to line-of-sight.
    protected virtual float DetectionRange => 32f;

    // Subclasses override AttackRange to set the range at which OnAttackEntity is called.
    protected virtual float AttackRange => 2.5f;

    private State mCurrentState = State.Idle;
    // True for the remainder of this tick once an attack has been triggered, so FaceMovementDirection() doesn't override the yaw set by FacePlayer().
    private bool mSuppressMovement;

    // Cached wander heading, refreshed periodically by Wander(); (0,0) means "stand still".
    private float mWanderDirX;
    private float mWanderDirZ;
    private int mWanderTimer;

    public HostileEntityAi(Entity entity) : base(entity)
    {
    }

    public override void Tick(World world)
    {
        // Getting hurt always forces an immediate switch to chasing, even without LOS (e.g. player hit the mob through a gap, or it's a ranged/behind-cover hit).
        if (WasHurt)
        {
            WasHurt = false;
            mCurrentState = State.Chasing;
            CurrentPath = null;
        }

        mSuppressMovement = false;

        Vector3 playerPos = Game.Instance.GetPlayer.Position;
        float dist = (playerPos - ParentEntity.Position).Length();

        bool inRange = dist <= DetectionRange;
        // Only raycast for LOS if already within range - raycasting is comparatively expensive.
        bool hasLos = inRange && HasLineOfSight(world, playerPos);

        if (hasLos)
        {
            mCurrentState = State.Chasing;
        }
        else if (mCurrentState == State.Chasing)
        {
            // Lost sight of the player mid-chase: fall back to wandering near the last known area rather than snapping straight back to Idle.
            mCurrentState = State.Wandering;
            CurrentPath = null;
        }

        switch (mCurrentState)
        {
            case State.Chasing:
                FacePlayer(playerPos);
                if (hasLos && dist <= AttackRange)
                {
                    // In attack range with clear LOS: stop horizontal movement (keep vertical/gravity velocity) and let the subclass perform its attack.
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

    /// <summary>
    /// Called every tick the entity is within AttackRange with a clear line of sight to the player. Base implementation is a no-op; subclasses (ZombieAi, SkeletonAi, SpiderAi) override this to deal melee damage, fire projectiles, etc. `dist` is the current straight-line distance to the player, useful for range-gated behaviours (e.g. spider's leap attack).
    /// </summary>
    protected virtual void OnAttackEntity(World world, float dist)
    {
    }

    // Rotates the entity to face the player directly (used while chasing/attacking, as opposed to FaceMovementDirection which faces the current velocity vector).
    private void FacePlayer(Vector3 playerPos)
    {
        float dx = playerPos.X - ParentEntity.Position.X;
        float dz = playerPos.Z - ParentEntity.Position.Z;
        float hLen = MathF.Sqrt(dx * dx + dz * dz);
        if (hLen > 0.01f)
            ParentEntity.Yaw = MathF.Atan2(dx, dz) - MathF.PI / 2f;
    }

    // Updates the chase target to the player's current block position and (re)paths to it, throttled by PATH_RECALC_TICKS so A* isn't run every single tick.
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

    // Advances the entity toward the next waypoint on CurrentPath, popping waypoints as they're reached and jumping when the path climbs a level or an obstacle is detected.
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
            // Close enough to the current waypoint - consume it and immediately aim at the next.
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

        // Normalize to a unit horizontal direction.
        dx /= dist;
        dz /= dist;

        float velY = ParentEntity.Velocity.Y;
        int currentY = (int)MathF.Floor(ParentEntity.Position.Y);
        // Jump if the path climbs to a higher block, or if a 1-block obstacle blocks the way.
        if (ParentEntity.IsOnGround && (waypoint.Y > currentY || ShouldJump(world, dx, dz)))
            velY = Physics.JUMP_VEL;

        ParentEntity.Velocity = new Vector3(dx * ParentEntity.WalkSpeed, velY, dz * ParentEntity.WalkSpeed);
    }

    // Random-walk movement used while State.Wandering (no LOS on player). Picks a new heading every 60-120 ticks (~3-6s); 40% chance to stand still instead of moving.
    private void Wander(World world)
    {
        mWanderTimer--;
        if (mWanderTimer <= 0)
        {
            mWanderTimer = 60 + Random.Next(60);
            if (Random.NextDouble() < 0.4)
            {
                // Stand still this cycle.
                mWanderDirX = 0f;
                mWanderDirZ = 0f;
            }
            else
            {
                // Pick a random horizontal heading.
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

        // Wander at half walk speed (unlike the full-speed chase movement).
        ParentEntity.Velocity = new Vector3(
            mWanderDirX * ParentEntity.WalkSpeed * 0.5f,
            velY,
            mWanderDirZ * ParentEntity.WalkSpeed * 0.5f);
    }

    // Raycasts from the mob's "eye" height toward the player's chest to determine if any solid block obstructs the view. Used to gate detection/aggro on actual visibility rather than just distance.
    private bool HasLineOfSight(World world, Vector3 playerPos)
    {
        // Eye height approximated as 90% of the mob's model height.
        Vector3 eye = ParentEntity.Position + new Vector3(0f, ParentEntity.Height * 0.9f, 0f);
        // Aim at roughly the player's chest (0.9 above their feet).
        Vector3 delta = playerPos + new Vector3(0f, 0.9f, 0f) - eye;
        float dist = delta.Length();
        if (dist < 0.01f) return true;
        var hit = world.RaycastBlocksOnly(eye, delta / dist, dist, solidOnly: true);
        // LOS is clear if the ray reaches the player without hitting a solid block first.
        return hit.Type != RaycastHitType.Block;
    }
}