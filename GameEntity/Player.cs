// Player entity — movement (survival, flying, swimming), input, fluid/fire damage, and environmental state | DA | 2/14/26


using VoxelEngine.Core;
using VoxelEngine.Terrain;
using VoxelEngine.Terrain.Blocks;

namespace VoxelEngine.GameEntity;

/// <summary>
/// The player-controlled entity. Owns the first-person <see cref="Camera"/>, drives the three mutually-exclusive movement modes (survival/walking, creative flying, swimming in water or lava), and tracks environmental status (breath, fire, drowning, fall damage, invincibility frames). Split across this file (movement/stats/update loop) and Player.Interaction.cs (block breaking, placement, and item use).
/// </summary>
public partial class Player : Entity
{
    // Ground/walk sprint speed is a straight multiplier on WalkSpeed (defined on Entity).
    private const float SPRINT_MULTIPLIER = 1.5f;
    private const float FLY_SPEED = 10.0f;
    private const float FLY_SPRINT_MULTIPLIER = 10f;

    // --- Swimming tuning constants (units: blocks/second unless noted) ---
    private const float SWIM_SPEED = 2.5f;
    private const float SWIM_UP_SPEED = 3.5f;
    private const float SWIM_SPRINT_MULT = 2.0f;
    private const float WATER_GRAVITY = 4.0f; // weaker than Physics.GRAVITY so the player sinks slowly
    private const float WATER_DRAG = 0.8f; // per-20Hz-tick velocity decay factor, see UpdateSwimming
    private const float WATER_TERMINAL = 4.0f;
    private const float WATER_JUMP_BOOST = 6.0f; // extra upward kick when jumping out of water into air

    public Camera Camera { get; }
    public bool IsFlying { get; private set; }
    public bool IsSprinting { get; private set; }
    // "Under" = the camera/eye position is inside the fluid block (affects vision/breathing). "In" = the player's feet are standing in the fluid block (affects movement mode selection).
    public bool IsUnderWater { get; private set; }
    public bool IsInWater { get; private set; }
    public bool IsUnderLava { get; private set; }
    public bool IsInLava { get; private set; }
    private bool IsSlowedDown { get; set; } // true while any block in [feet, eye] is a slowing block (e.g. cobweb, soul sand)
    private bool mWasInWater = false; // previous-frame IsInWater, used to detect the water-entry splash event
    private bool mWasInLava = false; // previous-frame IsInLava, used to detect the lava-entry splash event

    public float HorizontalSpeed { get; private set; } // blocks/second, recomputed each survival/swim tick; drives footstep timing

    private float mStepTimer = 0; // counts down in seconds until the next footstep sound/tick callback
    private float mStepInterval = .5f;

    private Vector3 mSpawnPosition;
    private BlockType mSelectedBlock = BlockType.Grass;
    private float mSmoothEyeY; // interpolated camera eye Y, lags behind the true eye height to smooth step-ups/downs

    private const float EYE_SMOOTH_SPEED = 16f; // exponential smoothing rate (higher = camera catches up faster)

    private float mInvincibilityTimer = 0f; // seconds remaining where TakeDamage is a no-op (i-frames)
    const float INVINCIBILITY_TIMER = .5f;
    private int mDamageRemainder; // sub-integer damage carried across hits
    public const int PLAYER_MAX_HEALTH = 20;


    private float mLavaDamageTimer = 0f; // seconds until the next tick of lava damage is applied
    private float mFireDamageTimer = 0f; // seconds until the next tick of burning damage is applied

    public const float BREATH_MAX = 15f; // seconds of breath available before drowning damage starts
    private float mBreathTimer = 0f;
    private float mDrownTimer = 0f;

    public float BreathFraction => mBreathTimer / BREATH_MAX;

    // Overrides Entity.Position so that moving the player also re-anchors the camera's X/Z. Y is intentionally driven by mSmoothEyeY instead of the raw position, to keep the smoothing from Update() in effect even when Position is set directly (e.g. teleports, physics resolution).
    public new Vector3 Position
    {
        get => base.Position;
        set
        {
            base.Position = value;
            Camera.Position = new Vector3(value.X, mSmoothEyeY, value.Z);
        }
    }

    public Player(Vector3 spawnPosition, float aspectRatio)
    {
        this.Health = 20;
        mSpawnPosition = spawnPosition;
        base.Position = spawnPosition;
        mSmoothEyeY = spawnPosition.Y + EyeHeight;
        Camera = new Camera(spawnPosition + new Vector3(0, EyeHeight, 0), aspectRatio);
        IsSlowedDown = false;

        mBreathTimer = BREATH_MAX;
        mInvincibilityTimer = 2f; // brief grace period so the player can't be hit immediately on spawn
    }

    // Entity's world-tick hook (fixed game-tick cadence) is unused for the player; all player logic runs from Update(), which is called every rendered frame with a variable deltaTime instead.
    public override void Tick(World world)
    {
    }

    /// <summary>
    /// Per-frame update: reads input, resolves environment state (water/lava/fire), dispatches to exactly one of the three movement modes (flying/swimming/survival), smooths the camera eye height, and applies periodic environmental damage (lava, fire, drowning). Called once per rendered frame with a variable deltaTime (seconds), not on the fixed world tick.
    /// </summary>
    public void Update(World world, float deltaTime)
    {
        HandleInput();
        UpdateUnderwaterState(world);

        // Fire one-shot "just entered fluid" effects by comparing to last frame's state.
        if (IsInWater && !mWasInWater)
        {
            Game.Instance.ParticleSystem.SpawnBlockBreakParticles(
                new Vector3i((int)MathF.Floor(Position.X), (int)MathF.Floor(Position.Y + 1),
                    (int)MathF.Floor(Position.Z)),
                BlockType.Water
            );

            Game.Instance.AudioManager.PlayBlockContactSound(BlockBreakMaterial.Water);
        }

        if (IsInLava && !mWasInLava)
        {
            Game.Instance.ParticleSystem.SpawnBlockBreakParticles(
                new Vector3i((int)MathF.Floor(Position.X), (int)MathF.Floor(Position.Y + 1),
                    (int)MathF.Floor(Position.Z)),
                BlockType.Lava
            );
        }

        mWasInWater = IsInWater;
        mWasInLava = IsInLava;

        if (IsFlying)
            UpdateFlying(world, deltaTime);
        else if (IsInWater || IsInLava)
            UpdateSwimming(world, deltaTime);
        else
            UpdateSurvival(world, deltaTime);

        // Smoothly interpolate camera Y to avoid jarring snaps on step-up/down
        float targetEyeY = base.Position.Y + EyeHeight;
        mSmoothEyeY = mSmoothEyeY + (targetEyeY - mSmoothEyeY) * MathF.Min(1f, EYE_SMOOTH_SPEED * deltaTime);
        Camera.Position = new Vector3(base.Position.X, mSmoothEyeY, base.Position.Z);

        if (mInvincibilityTimer > 0)
            mInvincibilityTimer -= deltaTime;

        Camera.UpdateShake(deltaTime);

        // Standing in lava deals damage every 0.5s and keeps the player continuously on fire (FireTimer is refreshed to 15s each tick rather than just set once on entry).
        if (IsInLava)
        {
            mLavaDamageTimer -= deltaTime;
            if (mLavaDamageTimer <= 0)
            {
                TakeDamage(2);
                mLavaDamageTimer = .5f;
            }

            FireTimer = 15f;
        }
        else
        {
            mLavaDamageTimer = 0f;
        }

        var footX = (int)MathF.Floor(Position.X);
        var footY = (int)MathF.Floor(Position.Y);
        var footZ = (int)MathF.Floor(Position.Z);
        // Standing directly in a fire block sets/extends the burn timer (but doesn't stack above the lava value).
        if (world.GetBlock(footX, footY, footZ) == BlockType.Fire)
            FireTimer = MathF.Max(FireTimer, 8f);

        // Water instantly extinguishes fire.
        if (IsInWater && FireTimer > 0f)
            FireTimer = 0f;

        // Burning deals 1 damage per second while FireTimer counts down to zero.
        if (FireTimer > 0f)
        {
            FireTimer -= deltaTime;
            mFireDamageTimer -= deltaTime;
            if (mFireDamageTimer <= 0f)
            {
                TakeDamage(1);
                mFireDamageTimer = 1f;
            }
        }
        else
        {
            mFireDamageTimer = 0f;
        }

        // Breath depletes only while the eye is submerged; once it hits zero, drowning damage ticks every 1s. Breath regenerates at 2x rate (faster than it depletes) once above water.
        if (IsUnderWater)
        {
            mBreathTimer -= deltaTime;
            if (mBreathTimer <= 0)
            {
                mDrownTimer -= deltaTime;
                if (mDrownTimer <= 0)
                {
                    TakeDamage(2);
                    mDrownTimer = 1f;
                }
            }
        }
        else
        {
            mBreathTimer = Math.Min(BREATH_MAX, mBreathTimer + deltaTime * 2f);
            mDrownTimer = 1f;
        }
    }

    /// <summary>
    /// Recomputes IsInWater/IsInLava (feet block) and IsUnderWater/IsUnderLava (camera/eye block), plus IsSlowedDown by scanning every block from feet to eye for slowing blocks (e.g. cobwebs). Must run before the movement-mode dispatch in Update(), since mode selection depends on IsInWater/IsInLava.
    /// </summary>
    public void UpdateUnderwaterState(World world)
    {
        var footX = (int)Math.Floor(Position.X);
        var footY = (int)Math.Floor(Position.Y);
        var footZ = (int)Math.Floor(Position.Z);

        var footBlock = world.GetBlock(footX, footY, footZ);
        IsInWater = (footBlock == BlockType.Water);
        IsInLava = (footBlock == BlockType.Lava);

        var eyeX = (int)MathF.Floor(Camera.Position.X);
        var eyeY = (int)MathF.Floor(Camera.Position.Y);
        var eyeZ = (int)MathF.Floor(Camera.Position.Z);

        var blockAtCamera = world.GetBlock(eyeX, eyeY, eyeZ);

        IsUnderWater = (blockAtCamera == BlockType.Water);
        IsUnderLava = (blockAtCamera == BlockType.Lava);

        IsSlowedDown = false;
        for (int y = footY; y <= eyeY; y++)
        {
            if (BlockRegistry.GetSlowsEntity(world.GetBlock(footX, y, footZ)))
            {
                IsSlowedDown = true;
                break;
            }
        }
    }

    /// <summary>
    /// Reads the movement-mode toggle and sprint state. Fly toggling uses IsKeyPressed (one-shot, fires once per key press) since it's a discrete toggle; sprint uses IsKeyDown (held) since it should stay active for as long as the key is held.
    /// </summary>
    private void HandleInput()
    {
        if (Game.Instance.IsKeyPressed(Keybindings.ToggleFly) && Game.Instance.IsCreative)
        {
            IsFlying = !IsFlying;
            if (IsFlying)
            {
                // Zero vertical velocity and reset fall tracking so toggling flight mid-fall doesn't carry over momentum or trigger fall damage later.
                Velocity = new Vector3(Velocity.X, 0, Velocity.Z);
                mFallDistance = 0f;
            }
        }

        IsSprinting = Game.Instance.IsKeyDown(Keybindings.Sprint);
    }

    /// <summary>
    /// Creative-mode free flight: no gravity, direct velocity-less movement along camera-relative axes plus explicit up/down keys, still collides with terrain via Physics.MoveWithCollision.
    /// </summary>
    private void UpdateFlying(World world, float deltaTime)
    {
        float speed = IsSprinting ? FLY_SPEED * FLY_SPRINT_MULTIPLIER : FLY_SPEED;
        Vector3 moveDir = GetMoveDirection();
        Vector3 movement = moveDir * speed * deltaTime;

        if (Game.Instance.IsKeyDown(Keybindings.Jump))
            movement.Y += speed * deltaTime;

        if (Game.Instance.IsKeyDown(Keybindings.FlyDown))
            movement.Y -= speed * deltaTime;

        Vector3 actual = Physics.MoveWithCollision(world, GetBoundingBox(), movement);
        Position += actual;
        IsOnGround = Physics.IsOnGround(world, GetBoundingBox());
    }

    /// <summary>
    /// Grounded/airborne movement: gravity + jump impulse, camera-relative horizontal walking, step-up collision resolution, fall-distance tracking for fall damage, and periodic footstep sound/callback triggering based on horizontal speed.
    /// </summary>
    private void UpdateSurvival(World world, float deltaTime)
    {
        var vel = Velocity;

        // Slowing blocks (cobweb, soul sand, etc.) reduce both gravity pull and terminal velocity, producing a "wading through molasses" feel rather than just capping horizontal speed.
        float grav = IsSlowedDown ? Physics.GRAVITY * 0.15f : Physics.GRAVITY;
        float termVel = IsSlowedDown ? 3f : Physics.TERMINAL_VEL;

        if (IsSlowedDown && vel.Y < -termVel)
            vel.Y = -termVel;

        vel.Y -= grav * deltaTime;
        vel.Y = MathF.Max(vel.Y, -termVel);
        Velocity = vel;

        // Jump uses IsKeyPressed (one-shot) so holding the key doesn't repeatedly re-trigger jumps; it's gated on IsOnGround so mid-air jump spam is impossible.
        if (Game.Instance.IsKeyPressed(Keybindings.Jump) && IsOnGround)
        {
            vel.Y = IsSlowedDown ? 3f : Physics.JUMP_VEL;
            Velocity = vel;
            IsOnGround = false;
        }

        float speed = IsSprinting ? WalkSpeed * SPRINT_MULTIPLIER : WalkSpeed;
        if (IsSlowedDown)
            speed *= 0.4f;

        Vector3 moveDir = GetMoveDirection();

        // Horizontal movement is speed-scaled per-frame (deltaTime-based), but vertical uses the already-integrated Velocity.Y from the gravity/jump step above.
        Vector3 frameVelocity = new Vector3(
            moveDir.X * speed,
            Velocity.Y,
            moveDir.Z * speed
        ) * deltaTime;

        float preCollisionVelY = Velocity.Y;
        // Only allow step-up assistance (auto-climbing 1-block-high ledges) while already grounded; mid-air collisions should not snap the player upward.
        float step = IsOnGround ? Physics.STEP_HEIGHT : 0f;
        Vector3 actual = Physics.MoveWithCollision(world, GetBoundingBox(), frameVelocity, step);
        Position += actual;

        bool wasOnGround = IsOnGround;
        // If the collision resolver shortened our vertical movement by more than ~1%, we hit something (floor or ceiling) this frame; disambiguate using the sign of Velocity.Y.
        if (MathF.Abs(actual.Y) < MathF.Abs(frameVelocity.Y) * 0.99f)
        {
            if (Velocity.Y < 0)
                IsOnGround = true;

            Velocity = new Vector3(Velocity.X, 0, Velocity.Z);
        }
        else
        {
            IsOnGround = Physics.IsOnGround(world, GetBoundingBox());
        }

        if (IsOnGround && !wasOnGround)
        {
            // Just landed
            if (mFallDistance > 0f)
                Fall(world, mFallDistance);

            mFallDistance = 0f;
        }
        else if (IsOnGround)
        {
            mFallDistance = 0f;
        }
        else if (preCollisionVelY < 0f)
        {
            // Accumulate fall distance only while actually descending, using pre-collision velocity so a landing frame's truncated velocity doesn't undercount the fall.
            mFallDistance += -preCollisionVelY * deltaTime;
        }

        // horizontalSpeed is derived from actual (post-collision) displacement, so walking into a wall correctly reports near-zero speed and suppresses footsteps.
        float horizontalSpeed = MathF.Sqrt(actual.X * actual.X + actual.Z * actual.Z) / deltaTime;
        HorizontalSpeed = horizontalSpeed;

        if (IsOnGround && horizontalSpeed > 0.2f)
        {
            mStepTimer -= deltaTime;

            if (mStepTimer <= 0)
            {
                mStepTimer = mStepInterval;

                // Sample slightly below the feet (Y - 0.05) to reliably land on the block being stood on rather than the block at the exact foot boundary.
                var blockBelowX = (int)MathF.Floor(Position.X);
                var blockBelowY = (int)MathF.Floor(Position.Y - 0.05f);
                var blockBelowZ = (int)MathF.Floor(Position.Z);

                var belowBlockType = world.GetBlock(blockBelowX, blockBelowY, blockBelowZ);
                var blockBelowMat = BlockRegistry.GetBlockBreakMaterial(belowBlockType);
                Game.Instance.AudioManager.PlayBlockContactSound(blockBelowMat);
                BlockRegistry.Get(belowBlockType).OnEntityWalking(world, blockBelowX, blockBelowY, blockBelowZ, Game.Instance.GameRandom);
            }
        }
        else
        {
            mStepTimer = 0;
        }
    }

    /// <summary>
    /// Builds a normalized movement direction in world space from WASD-style input, projected onto the camera's forward/right vectors flattened to the horizontal plane (Y=0) so looking up/down doesn't change walk speed or direction.
    /// </summary>
    private Vector3 GetMoveDirection()
    {
        Vector3 forward = Camera.Front;
        forward.Y = 0;

        if (forward.LengthSquared() > 0.001f)
            forward = Vector3.Normalize(forward);

        Vector3 right = Camera.Right;
        right.Y = 0;

        if (right.LengthSquared() > 0.001f)
            right = Vector3.Normalize(right);

        Vector3 dir = Vector3.Zero;

        if (Game.Instance.IsKeyDown(Keybindings.MoveForward))
            dir += forward;

        if (Game.Instance.IsKeyDown(Keybindings.MoveBack))
            dir -= forward;

        if (Game.Instance.IsKeyDown(Keybindings.MoveLeft))
            dir -= right;

        if (Game.Instance.IsKeyDown(Keybindings.MoveRight))
            dir += right;

        return dir.LengthSquared() > 0 ? Vector3.Normalize(dir) : dir;
    }

    /// <summary>
    /// Movement while submerged in water or lava: weaker gravity, velocity-based drag instead of hard-capped speed, a special jump that launches the player out of the water surface when their head is no longer in a water block, and no fall-distance accumulation (no fall damage while swimming).
    /// </summary>
    private void UpdateSwimming(World world, float deltaTime)
    {
        mFallDistance = 0f;
        var vel = Velocity;
        vel.Y -= WATER_GRAVITY * deltaTime;
        vel.Y = Math.Max(vel.Y, -WATER_TERMINAL);

        // Jump uses IsKeyDown (held) here rather than IsKeyPressed, so holding space lets the player continuously paddle upward/out of the water.
        if (Game.Instance.IsKeyDown(Keybindings.Jump))
        {
            var headX = (int)MathF.Floor(Position.X);
            var headY = (int)MathF.Floor(Position.Y);
            var headZ = (int)MathF.Floor(Position.Z);
            var headBlock = world.GetBlock(headX, headY, headZ);

            // If the head has broken the surface (no longer in water), give a bigger upward boost to hop out onto land/a boat; otherwise just paddle up at normal swim speed.
            if (headBlock != BlockType.Water)
                vel.Y = WATER_JUMP_BOOST;
            else
                vel.Y = SWIM_UP_SPEED;
        }


        if (Game.Instance.IsKeyDown(Keybindings.FlyDown))
            vel.Y = -SWIM_UP_SPEED;

        // Exponential drag: WATER_DRAG (0.8) is the decay factor per 1/20s "tick", so scaling the exponent by deltaTime*20 makes the damping frame-rate independent while matching the original per-tick (20Hz) balance the constant was tuned against.
        vel.X *= MathF.Pow(WATER_DRAG, deltaTime * 20);
        vel.Y *= MathF.Pow(WATER_DRAG, deltaTime * 20);
        vel.Z *= MathF.Pow(WATER_DRAG, deltaTime * 20);

        Velocity = vel;

        var speed = IsSprinting ? SWIM_SPEED * SWIM_SPRINT_MULT : SWIM_SPEED;
        var moveDir = GetMoveDirection();

        Vector3 frameVelocity = new Vector3(
            moveDir.X * speed,
            Velocity.Y,
            moveDir.Z * speed
        ) * deltaTime;

        var actual = Physics.MoveWithCollision(world, GetBoundingBox(), frameVelocity);
        Position += actual;

        if (MathF.Abs(actual.Y) < MathF.Abs(frameVelocity.Y) * .99)
        {
            if (Velocity.Y < 0)
                IsOnGround = true;

            Velocity = new Vector3(Velocity.X, 0, Velocity.Z);
        }
        else
        {
            IsOnGround = Physics.IsOnGround(world, GetBoundingBox());
        }
    }

    public void HandleMouseLook(Vector2 delta) => Camera.Rotate(delta.X, delta.Y);

    /// <summary>Teleports the player back to their world spawn point and resets velocity/rotation/ground state (used on death/respawn).</summary>
    public void ResetPosition()
    {
        base.Position = mSpawnPosition;
        mSmoothEyeY = mSpawnPosition.Y + EyeHeight;
        Camera.Position = new Vector3(mSpawnPosition.X, mSmoothEyeY, mSpawnPosition.Z);
        Velocity = Vector3.Zero;
        IsOnGround = false;
        Camera.SetRotation(0, -90);
    }

    /// <summary>
    /// Called once when the player transitions from falling to grounded (see UpdateSurvival). Plays a landing sound based on the block underfoot and applies fall damage for falls over 3 blocks (damage = ceil(distance - 3), so a 4-block fall deals 1 damage).
    /// </summary>
    protected override void Fall(World world, float dist)
    {
        if (dist < 1f)
            return;

        // Sample slightly below the feet (Y - 0.2) to land on the block just walked onto.
        var bx = (int)MathF.Floor(Position.X);
        var by = (int)MathF.Floor(Position.Y - 0.2f);
        var bz = (int)MathF.Floor(Position.Z);
        var mat = BlockRegistry.GetBlockBreakMaterial(world.GetBlock(bx, by, bz));
        Game.Instance.AudioManager.PlayLandingSound(mat);

        int damage = (int)MathF.Ceiling(dist - 3f);
        if (damage > 0)
            TakeDamage(damage);
    }

    /// <summary>
    /// Applies damage to the player, respecting creative-mode invulnerability and post-hit invincibility frames, and reducing damage via equipped armor (Minecraft-style formula: each armor point reduces damage by 4%, i.e. damage * (25 - armorValue) / 25, with the fractional remainder carried over to the next hit via mDamageRemainder so repeated small hits aren't rounded away to zero forever).
    /// </summary>
    public override void TakeDamage(int amount)
    {
        if (Game.Instance.IsCreative)
            return;

        if (mInvincibilityTimer > 0)
            return;


        var inv = Game.Instance.PlayerInventory;
        if (inv != null)
        {
            int armorValue = inv.GetArmorValue();
            // Integer math with a carried remainder: scaledDamage/25 gives whole damage, and the remainder is kept in mDamageRemainder so damage isn't silently lost to rounding when armor reduces a hit below 1 whole point.
            int scaledDamage = amount * (25 - armorValue) + mDamageRemainder;
            int actualDamage = scaledDamage / 25;
            mDamageRemainder = scaledDamage % 25;

            inv.DamageArmor(amount);

            if (actualDamage == 0)
            {
                // Armor fully absorbed this hit (this time) — still grant i-frames so the player isn't hit again instantly, but skip health loss and effects.
                mInvincibilityTimer = INVINCIBILITY_TIMER;
                return;
            }

            amount = actualDamage;
        }

        Health = Math.Max(0, Health - amount);
        mInvincibilityTimer = INVINCIBILITY_TIMER;
        Camera.Shake(0.4f);

        Game.Instance.AudioManager.PlayPlayerHurtSound();
    }

    /// <summary>Restores health up to PLAYER_MAX_HEALTH (does not grant invincibility frames).</summary>
    public void Heal(int amount)
    {
        this.Health = Math.Min(PLAYER_MAX_HEALTH, Health + amount);
    }
}
