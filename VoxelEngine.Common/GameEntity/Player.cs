// Player entity — movement (survival, flying, swimming), input, fluid/fire damage, and environmental state | DA | 2/14/26


using VoxelEngine.Core;
using VoxelEngine.Terrain;
using VoxelEngine.Terrain.Blocks;

namespace VoxelEngine.GameEntity;

/// <summary>
/// The player-controlled entity. Owns the first-person <see cref="Camera"/>, drives the three
/// mutually-exclusive movement modes (survival/walking, creative flying, swimming in water or lava),
/// and tracks environmental status (breath, fire, drowning, fall damage, invincibility frames).
/// Split across this file (movement/stats/tick loop) and Player.Interaction.cs (block breaking,
/// placement, and item use).
///
/// Movement follows Beta 1.7.3's <c>moveEntityWithHeading</c>: velocity accumulates, and friction
/// (not the input) is what brings the player to a stop. Everything here runs on the fixed 20 Hz
/// tick, because every constant below is a per-tick multiplier and only means what it means at
/// that rate. <see cref="UpdateVisual"/> handles the per-frame half (camera placement, bob, FOV).
/// </summary>
public partial class Player : Entity
{
    /// <summary>
    /// This player's movement intent for the current tick. The host writes it; the movement code
    /// below only reads it.
    ///
    /// On the client, <c>Game</c> fills this from the keyboard each frame before ticking. On a
    /// server it's filled from the player's movement packets, and stays <see cref="PlayerInput.None"/>
    /// for a player that hasn't reported in - which makes them stand still rather than drift.
    /// </summary>
    public PlayerInput Input;

    // --- Beta 1.7.3 movement constants ------------------------------------------------------
    //
    // All in blocks *per tick*, straight out of b1.7.3's EntityLiving/EntityPlayer. Velocity is
    // still stored in blocks/second (Entity's contract, and what knockback and the network use),
    // so the tick converts in and out around this maths.

    private const float GRAVITY_PER_TICK = 0.08f;   // motionY -= 0.08 each tick, applied after moving
    private const float VERTICAL_DRAG = 0.98f;      // motionY *= 0.98 each tick
    private const float JUMP_MOTION = 0.42f;        // motionY on a ground jump; gives the familiar ~1.25 block hop
    private const float AIR_FRICTION = 0.91f;       // horizontal damping while airborne
    private const float GROUND_ACCEL = 0.1f;        // landMovementFactor: input strength while grounded
    private const float AIR_ACCEL = 0.02f;          // jumpMovementFactor: the much weaker mid-air steering
    private const float FLUID_ACCEL = 0.02f;        // input strength while swimming

    // 0.546^3, where 0.546 is the default ground friction (0.6 slipperiness * 0.91). Ground
    // acceleration is divided by the cube of the actual friction so that top speed stays put
    // across surfaces and only the time-to-reach-it changes - which is exactly why ice feels
    // slippery rather than simply fast.
    private const float ACCEL_FRICTION_NORM = 0.16277136f;

    private const float WATER_DRAG = 0.8f;          // per-tick damping on all three axes in water
    private const float LAVA_DRAG = 0.5f;           // lava is thicker, so it damps harder
    private const float FLUID_GRAVITY = 0.02f;      // much weaker sink than air gravity
    private const float FLUID_SWIM_MOTION = 0.04f;  // added per tick while holding jump underwater
    private const float FLUID_LEDGE_HOP = 0.3f;     // upward kick when swimming into a wall, to climb out onto land

    private const float SNEAK_INPUT_SCALE = 0.3f;   // sneaking scales movement input, not the resulting speed
    private const float SLOWED_JUMP_MOTION = 0.15f; // cobweb/soul-sand jump

    // Not vanilla b1.7.3 (sprinting arrived in Beta 1.8), kept because it's wired into the
    // keybindings and HUD. Applied as a multiplier on ground acceleration, the same way 1.8 does
    // it, plus the forward kick on takeoff that makes sprint-jumping outrun plain sprinting.
    private const float SPRINT_ACCEL_MULT = 1.3f;
    private const float SPRINT_JUMP_KICK = 0.2f;

    private const float FLY_SPEED = 10.0f;
    private const float FLY_SPRINT_MULTIPLIER = 10f;

    public Camera Camera { get; }
    public bool IsFlying { get; private set; }
    public bool IsSprinting { get; private set; }
    public bool IsSneaking { get; private set; }
    // "Under" = the camera/eye position is inside the fluid block (affects vision/breathing).
    // "In" = the player's feet are standing in the fluid block (affects movement mode selection).
    public bool IsUnderWater { get; private set; }
    public bool IsInWater { get; private set; }
    public bool IsUnderLava { get; private set; }
    public bool IsInLava { get; private set; }
    private bool IsSlowedDown { get; set; } // true while any block in [feet, eye] is a slowing block (e.g. cobweb, soul sand)
    private bool mWasInWater = false; // previous-tick IsInWater, used to detect the water-entry splash event
    private bool mWasInLava = false; // previous-tick IsInLava, used to detect the lava-entry splash event

    public float HorizontalSpeed { get; private set; } // blocks/second, recomputed each tick; drives the arm bob

    // Sneaking drops the eye 0.08 blocks, as in vanilla - small, but it's most of what makes
    // sneaking read as sneaking from first person.
    private const float SNEAK_EYE_DROP = 0.08f;
    public override float EyeHeight
    {
        get => IsSneaking ? 1.62f - SNEAK_EYE_DROP : 1.62f;
        set { }
    }

    // Distance walked, used for footstep timing and camera bob. b1.7.3 tracks this instead of a
    // timer, which is why footsteps and bob stay in step with each other at any speed.
    private float mWalkDistance;
    private float mPrevWalkDistance;
    private int mNextStepDistance = 1;

    private Vector3 mSpawnPosition;
    private BlockType mSelectedBlock = BlockType.Grass;

    // Position at the start of the current tick. Rendering lerps between this and the live
    // position by the partial tick, so a 20 Hz simulation still draws smoothly at any framerate.
    private Vector3 mPrevPosition;

    // Vanilla pops the camera instantly when you step up a slab or stair. That reads as a jolt at
    // high framerates, so the climb is eased over a few frames instead. Set to 0 for the raw
    // b1.7.3 behaviour.
    private const float STEP_SMOOTH_SPEED = 24f;
    private float mStepSmoothOffset; // remaining un-applied part of the last step-up, in blocks

    /// Set while connected to a server: damage, death and respawn are all its decision.
    public bool ServerOwnsHealth;

    /// Set on the server instead: mob AI still calls TakeDamage on this entity, and the host has to
    /// be the one that applies it, so it can send the health packet and handle dying.
    public Action<int>? DamageHandler;

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

    // Overrides Entity.Position so that teleports (respawn, network corrections) also re-anchor the
    // camera and collapse the render interpolation - otherwise the player would visibly slide from
    // the old position to the new one over the following frames instead of arriving.
    public new Vector3 Position
    {
        get => base.Position;
        set
        {
            base.Position = value;
            mPrevPosition = value;
            mStepSmoothOffset = 0f;
            Camera.Position = new Vector3(value.X, value.Y + EyeHeight, value.Z);
        }
    }

    public Player(Vector3 spawnPosition, float aspectRatio)
    {
        this.Health = 20;
        mSpawnPosition = spawnPosition;
        base.Position = spawnPosition;
        mPrevPosition = spawnPosition;
        Camera = new Camera(spawnPosition + new Vector3(0, 1.62f, 0), aspectRatio);
        IsSlowedDown = false;

        mBreathTimer = BREATH_MAX;
        mInvincibilityTimer = 2f; // brief grace period so the player can't be hit immediately on spawn
    }

    /// <summary>
    /// One 20 Hz simulation step: environment state, movement, and the periodic environmental
    /// damage timers. This is Entity's tick hook, so the player now runs on the same clock as
    /// every mob - which is the only rate at which the per-tick friction constants above are
    /// correct.
    /// </summary>
    public override void Tick(World world)
    {
        const float dt = TickSystem.TICK_DURATION;

        mPrevPosition = base.Position;
        mPrevWalkDistance = mWalkDistance;

        HandleInput();
        UpdateUnderwaterState(world);

        // Fire one-shot "just entered fluid" effects by comparing to last tick's state.
        if (IsInWater && !mWasInWater)
        {
            GameContext.Current.ParticleSystem.SpawnBlockBreakParticles(
                new Vector3i((int)MathF.Floor(base.Position.X), (int)MathF.Floor(base.Position.Y + 1),
                    (int)MathF.Floor(base.Position.Z)),
                BlockType.Water
            );

            GameContext.Current.AudioManager.PlayBlockContactSound(BlockBreakMaterial.Water);
        }

        if (IsInLava && !mWasInLava)
        {
            GameContext.Current.ParticleSystem.SpawnBlockBreakParticles(
                new Vector3i((int)MathF.Floor(base.Position.X), (int)MathF.Floor(base.Position.Y + 1),
                    (int)MathF.Floor(base.Position.Z)),
                BlockType.Lava
            );
        }

        mWasInWater = IsInWater;
        mWasInLava = IsInLava;

        if (IsFlying)
            TickFlying(world);
        else if (IsInWater)
            TickFluid(world, WATER_DRAG);
        else if (IsInLava)
            TickFluid(world, LAVA_DRAG);
        else
            TickWalking(world);

        if (mInvincibilityTimer > 0)
            mInvincibilityTimer -= dt;

        // Standing in lava deals damage every 0.5s and keeps the player continuously on fire
        // (FireTimer is refreshed to 15s each tick rather than just set once on entry).
        if (IsInLava)
        {
            mLavaDamageTimer -= dt;
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

        var footX = (int)MathF.Floor(base.Position.X);
        var footY = (int)MathF.Floor(base.Position.Y);
        var footZ = (int)MathF.Floor(base.Position.Z);
        // Standing directly in a fire block sets/extends the burn timer (but doesn't stack above the lava value).
        if (world.GetBlock(footX, footY, footZ) == BlockType.Fire)
            FireTimer = MathF.Max(FireTimer, 8f);

        // Water instantly extinguishes fire.
        if (IsInWater && FireTimer > 0f)
            FireTimer = 0f;

        // Burning deals 1 damage per second while FireTimer counts down to zero.
        if (FireTimer > 0f)
        {
            FireTimer -= dt;
            mFireDamageTimer -= dt;
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

        // Breath depletes only while the eye is submerged; once it hits zero, drowning damage
        // ticks every 1s. Breath regenerates at 2x rate (faster than it depletes) once above water.
        if (IsUnderWater)
        {
            mBreathTimer -= dt;
            if (mBreathTimer <= 0)
            {
                mDrownTimer -= dt;
                if (mDrownTimer <= 0)
                {
                    TakeDamage(2);
                    mDrownTimer = 1f;
                }
            }
        }
        else
        {
            mBreathTimer = Math.Min(BREATH_MAX, mBreathTimer + dt * 2f);
            mDrownTimer = 1f;
        }
    }

    /// <summary>
    /// Per-frame visual update, separate from the simulation above. Places the camera at the eye
    /// position interpolated between the last two ticks (so 20 Hz movement draws smoothly), eases
    /// out any pending step-up, and advances the bob/FOV animations.
    /// </summary>
    /// <param name="partialTick">Progress in [0,1) from the previous tick to the current one.</param>
    public void UpdateVisual(float deltaTime, float partialTick)
    {
        Vector3 eye = Vector3.Lerp(mPrevPosition, base.Position, Math.Clamp(partialTick, 0f, 1f));

        if (mStepSmoothOffset != 0f)
        {
            // Decay what's left of the step toward zero; the camera trails the feet by that much.
            float decay = MathF.Min(1f, STEP_SMOOTH_SPEED * deltaTime);
            mStepSmoothOffset -= mStepSmoothOffset * decay;
            if (MathF.Abs(mStepSmoothOffset) < 0.001f)
                mStepSmoothOffset = 0f;
        }

        Camera.Position = new Vector3(eye.X, eye.Y + EyeHeight - mStepSmoothOffset, eye.Z);

        // Bob is driven by distance walked rather than time, so it stays locked to the footsteps.
        float walk = mPrevWalkDistance + (mWalkDistance - mPrevWalkDistance) * Math.Clamp(partialTick, 0f, 1f);
        Camera.UpdateBob(walk, HorizontalSpeed, IsOnGround && !IsFlying, deltaTime);
        Camera.UpdateFov(IsSprinting && Input.HasMovement && !IsFlying, deltaTime);
        Camera.UpdateShake(deltaTime);
    }

    /// <summary>
    /// Recomputes IsInWater/IsInLava (feet block) and IsUnderWater/IsUnderLava (camera/eye block),
    /// plus IsSlowedDown by scanning every block from feet to eye for slowing blocks (e.g. cobwebs).
    /// Must run before the movement-mode dispatch in Tick(), since mode selection depends on
    /// IsInWater/IsInLava.
    /// </summary>
    public void UpdateUnderwaterState(World world)
    {
        var footX = (int)Math.Floor(base.Position.X);
        var footY = (int)Math.Floor(base.Position.Y);
        var footZ = (int)Math.Floor(base.Position.Z);

        // Fluids have levels now, so being "in" one means being below its actual surface - a
        // one-ninth-deep film running over the ground is a puddle to walk through, not water to
        // swim in.
        IsInWater = BlockFluid.ContainsPoint(world, BlockType.Water, base.Position);
        IsInLava = BlockFluid.ContainsPoint(world, BlockType.Lava, base.Position);

        // Derived from the simulated position rather than read off the Camera: the camera is placed
        // once per frame from interpolated state, so during a tick it can be a frame behind, and
        // whether the player is drowning shouldn't depend on the render clock.
        var eye = base.Position with { Y = base.Position.Y + EyeHeight };

        IsUnderWater = BlockFluid.ContainsPoint(world, BlockType.Water, eye);
        IsUnderLava = BlockFluid.ContainsPoint(world, BlockType.Lava, eye);

        IsSlowedDown = false;
        int eyeBlockY = (int)MathF.Floor(eye.Y);
        for (int y = footY; y <= eyeBlockY; y++)
        {
            if (BlockRegistry.GetSlowsEntity(world.GetBlock(footX, y, footZ)))
            {
                IsSlowedDown = true;
                break;
            }
        }
    }

    /// <summary>
    /// Reads the movement-mode toggles. Fly toggling is edge-triggered (fires once per press) since
    /// it's a discrete toggle; sprint and sneak are held states.
    /// </summary>
    private void HandleInput()
    {
        if (Input.ToggleFly && GameContext.Current.IsCreative)
        {
            IsFlying = !IsFlying;
            if (IsFlying)
            {
                // Zero vertical velocity and reset fall tracking so toggling flight mid-fall
                // doesn't carry over momentum or trigger fall damage later.
                Velocity = new Vector3(Velocity.X, 0, Velocity.Z);
                mFallDistance = 0f;
            }
        }

        IsSprinting = Input.Sprint && !Input.Sneak;
        IsSneaking = Input.Sneak && !IsFlying;
    }

    /// <summary>
    /// b1.7.3's <c>moveFlying</c>: turns the four directional keys into an acceleration added to
    /// the current motion, rotated into world space by the camera yaw. The input vector is scaled
    /// down to unit length only when it exceeds it, so walking diagonally is neither faster nor
    /// slower than walking straight.
    /// </summary>
    private Vector3 ApplyMoveInput(Vector3 motion, float accel)
    {
        float strafe = 0f, forward = 0f;

        if (Input.MoveForward) forward += 1f;
        if (Input.MoveBack) forward -= 1f;
        if (Input.MoveRight) strafe += 1f;
        if (Input.MoveLeft) strafe -= 1f;

        // Sneaking scales the input, not the result: acceleration and friction are unchanged, so
        // you still coast to a stop the same way, just from a lower top speed.
        if (IsSneaking)
        {
            strafe *= SNEAK_INPUT_SCALE;
            forward *= SNEAK_INPUT_SCALE;
        }

        float lenSq = strafe * strafe + forward * forward;
        if (lenSq < 1e-4f)
            return motion;

        float len = MathF.Sqrt(lenSq);
        if (len < 1f)
            len = 1f;

        float scale = accel / len;
        strafe *= scale;
        forward *= scale;

        // Horizontal forward is (cos yaw, sin yaw) in XZ and right is its perpendicular, matching
        // Camera.Front/Camera.Right flattened - so aiming and walking agree.
        float yaw = float.DegreesToRadians(Camera.Yaw);
        float cos = MathF.Cos(yaw), sin = MathF.Sin(yaw);

        motion.X += forward * cos - strafe * sin;
        motion.Z += forward * sin + strafe * cos;
        return motion;
    }

    /// <summary>
    /// Creative free flight: no gravity, direct positional movement along camera-relative axes plus
    /// explicit up/down keys. Not a vanilla b1.7.3 mode (creative arrived in 1.8), so it keeps its
    /// own simple speed-based handling rather than the friction model.
    /// </summary>
    private void TickFlying(World world)
    {
        const float dt = TickSystem.TICK_DURATION;
        float speed = IsSprinting ? FLY_SPEED * FLY_SPRINT_MULTIPLIER : FLY_SPEED;

        Vector3 dir = Vector3.Zero;
        Vector3 forward = Camera.Front with { Y = 0 };
        Vector3 right = Camera.Right with { Y = 0 };

        if (forward.LengthSquared() > 0.001f) forward = Vector3.Normalize(forward);
        if (right.LengthSquared() > 0.001f) right = Vector3.Normalize(right);

        if (Input.MoveForward) dir += forward;
        if (Input.MoveBack) dir -= forward;
        if (Input.MoveRight) dir += right;
        if (Input.MoveLeft) dir -= right;

        if (dir.LengthSquared() > 0f)
            dir = Vector3.Normalize(dir);

        Vector3 movement = dir * speed * dt;

        if (Input.Jump)
            movement.Y += speed * dt;

        if (Input.Descend || Input.Sneak)
            movement.Y -= speed * dt;

        Vector3 actual = Physics.MoveWithCollision(world, GetBoundingBox(), movement);
        base.Position += actual;
        IsOnGround = Physics.IsOnGround(world, GetBoundingBox());
        Velocity = Vector3.Zero;
        HorizontalSpeed = MathF.Sqrt(actual.X * actual.X + actual.Z * actual.Z) / dt;
    }

    /// <summary>
    /// Grounded/airborne movement, following b1.7.3's order exactly: sample friction from the block
    /// underfoot, jump, accelerate from input, move and resolve collisions, then apply gravity and
    /// friction to what's left. Momentum survives across ticks, which is what makes stopping,
    /// turning and sprint-jumping feel the way they do.
    /// </summary>
    private void TickWalking(World world)
    {
        const float dt = TickSystem.TICK_DURATION;

        // Friction is sampled from the ground state *before* moving, as vanilla does - so the tick
        // you leave a ledge still gets ground friction, and the tick you land still gets air.
        float friction = AIR_FRICTION;
        if (IsOnGround)
        {
            var box = GetBoundingBox();
            var below = world.GetBlock(
                (int)MathF.Floor(base.Position.X),
                (int)MathF.Floor(box.Min.Y) - 1,
                (int)MathF.Floor(base.Position.Z));
            friction = BlockRegistry.GetSlipperiness(below) * AIR_FRICTION;
        }

        float accel = IsOnGround
            ? GROUND_ACCEL * (ACCEL_FRICTION_NORM / (friction * friction * friction))
            : AIR_ACCEL;

        if (IsOnGround && IsSprinting)
            accel *= SPRINT_ACCEL_MULT;

        if (IsSlowedDown)
            accel *= 0.4f;

        // Velocity is stored in blocks/second for everyone else (knockback, the network, mob AI);
        // the maths below is all per-tick, so convert on the way in and back out.
        Vector3 motion = Velocity * dt;

        bool wasOnGround = IsOnGround;

        if (Input.JumpPressed && IsOnGround)
        {
            motion.Y = IsSlowedDown ? SLOWED_JUMP_MOTION : JUMP_MOTION;

            // The sprint-jump kick: a one-off shove along the facing, so a sprint-jump chain covers
            // more ground than sprinting flat. (Beta 1.8 behaviour, kept alongside sprint itself.)
            if (IsSprinting && Input.HasMovement)
            {
                float yaw = float.DegreesToRadians(Camera.Yaw);
                motion.X += MathF.Cos(yaw) * SPRINT_JUMP_KICK;
                motion.Z += MathF.Sin(yaw) * SPRINT_JUMP_KICK;
            }

            IsOnGround = false;
        }

        motion = ApplyMoveInput(motion, accel);

        float preCollisionMotionY = motion.Y;
        // Step-up assistance (auto-climbing slabs/stairs) only while grounded; a mid-air collision
        // must not snap the player upward.
        float step = wasOnGround ? Physics.STEP_HEIGHT : 0f;
        float feetBefore = base.Position.Y;

        Vector3 actual = Physics.MoveWithCollision(world, GetBoundingBox(), motion, step, IsSneaking && wasOnGround);
        base.Position += actual;

        // A step-up is the only way the feet rise on a tick where gravity was pulling them down.
        // Hand that rise to the camera smoother so the view climbs over a few frames instead of
        // snapping. (Comparing against the *requested* motion.Y instead would fire every tick on
        // flat ground, where the floor cancels the 0.08 of gravity.)
        float climbed = base.Position.Y - feetBefore;
        if (STEP_SMOOTH_SPEED > 0f && wasOnGround && climbed > 0.01f && motion.Y <= 0f)
            mStepSmoothOffset = MathF.Min(mStepSmoothOffset + climbed, Physics.STEP_HEIGHT);

        // If the resolver shortened our vertical travel by more than ~1%, we hit a floor or ceiling
        // this tick; the sign of the attempted motion says which.
        if (MathF.Abs(actual.Y) < MathF.Abs(motion.Y) * 0.99f)
        {
            if (motion.Y < 0)
                IsOnGround = true;

            motion.Y = 0f;
        }
        else
        {
            IsOnGround = Physics.IsOnGround(world, GetBoundingBox());
        }

        // Running into a wall kills the motion into it, rather than letting it build up invisibly
        // and fire the player sideways the moment the wall ends.
        if (MathF.Abs(actual.X) < MathF.Abs(motion.X) * 0.99f) motion.X = 0f;
        if (MathF.Abs(actual.Z) < MathF.Abs(motion.Z) * 0.99f) motion.Z = 0f;

        // Gravity and drag are applied *after* the move, so the first tick of a jump travels the
        // full jump impulse. Getting this order wrong is what shortens the classic 1.25-block hop.
        motion.Y -= IsSlowedDown ? GRAVITY_PER_TICK * 0.15f : GRAVITY_PER_TICK;
        motion.Y *= VERTICAL_DRAG;

        float terminal = IsSlowedDown ? 0.15f : Physics.TERMINAL_VEL * dt;
        if (motion.Y < -terminal)
            motion.Y = -terminal;

        motion.X *= friction;
        motion.Z *= friction;

        Velocity = motion / dt;

        TrackFallAndSteps(world, actual, preCollisionMotionY, wasOnGround, dt);
    }

    /// <summary>
    /// Swimming in water or lava: weak gravity, heavy drag on every axis, and much weaker input
    /// acceleration than on land. The drag is what does the work here - unlike the land model it
    /// alone sets the top speed, which is why water feels like water.
    /// </summary>
    private void TickFluid(World world, float drag)
    {
        const float dt = TickSystem.TICK_DURATION;

        mFallDistance = 0f;
        Vector3 motion = Velocity * dt;
        bool wasOnGround = IsOnGround;

        motion = ApplyMoveInput(motion, FLUID_ACCEL);

        // A current carries you. This is small per tick, but it accumulates against the fluid's own
        // drag into a real drift, which is what makes a river read as flowing rather than as a
        // still trench that happens to be water-shaped.
        var fluid = IsInLava ? BlockType.Lava : BlockType.Water;
        var flow = BlockFluid.FlowDirection(world,
            (int)MathF.Floor(base.Position.X),
            (int)MathF.Floor(base.Position.Y),
            (int)MathF.Floor(base.Position.Z),
            fluid);

        motion += flow * BlockFluid.FLOW_PUSH_PER_TICK;

        // Holding jump paddles upward a little each tick; against 0.02 gravity and 0.8 drag that
        // settles at a slow, floaty rise rather than an immediate launch.
        if (Input.Jump)
            motion.Y += FLUID_SWIM_MOTION;

        if (Input.Descend || Input.Sneak)
            motion.Y -= FLUID_SWIM_MOTION;

        Vector3 actual = Physics.MoveWithCollision(world, GetBoundingBox(), motion);
        base.Position += actual;

        bool blockedHorizontally = MathF.Abs(actual.X) < MathF.Abs(motion.X) * 0.99f
                                   || MathF.Abs(actual.Z) < MathF.Abs(motion.Z) * 0.99f;

        if (MathF.Abs(actual.Y) < MathF.Abs(motion.Y) * 0.99f)
        {
            if (motion.Y < 0)
                IsOnGround = true;

            motion.Y = 0f;
        }
        else
        {
            IsOnGround = Physics.IsOnGround(world, GetBoundingBox());
        }

        motion *= drag;
        motion.Y -= FLUID_GRAVITY;

        // Swimming into the wall at the water's edge hops you up it, which is how you climb out of
        // a lake in vanilla instead of being stuck against the bank.
        if (blockedHorizontally && Input.Jump)
            motion.Y = FLUID_LEDGE_HOP;

        Velocity = motion / dt;

        TrackFallAndSteps(world, actual, 0f, wasOnGround, dt);
    }

    /// <summary>
    /// Shared post-move bookkeeping: fall-distance accumulation and landing damage, walk distance,
    /// and distance-based footstep sounds. b1.7.3 fires footsteps every whole block walked rather
    /// than on a timer, so they naturally keep pace with however fast the player is moving.
    /// </summary>
    private void TrackFallAndSteps(World world, Vector3 actual, float preCollisionMotionY, bool wasOnGround, float dt)
    {
        if (IsOnGround && !wasOnGround)
        {
            if (mFallDistance > 0f)
                Fall(world, mFallDistance);

            mFallDistance = 0f;
        }
        else if (IsOnGround)
        {
            mFallDistance = 0f;
        }
        else if (preCollisionMotionY < 0f)
        {
            // Accumulate only while actually descending, using the pre-collision motion so a
            // landing tick's truncated travel doesn't undercount the fall.
            mFallDistance += -preCollisionMotionY;
        }

        float horizontal = MathF.Sqrt(actual.X * actual.X + actual.Z * actual.Z);
        HorizontalSpeed = horizontal / dt;

        // The 0.6 factor is vanilla's: it makes one unit of walk distance land near one block, and
        // it's what the bob amplitude below is tuned against.
        mWalkDistance += horizontal * 0.6f;

        if (!IsOnGround || horizontal < 0.001f)
            return;

        if (mWalkDistance <= mNextStepDistance)
            return;

        mNextStepDistance = (int)mWalkDistance + 1;

        // Sample slightly below the feet so we land on the block being stood on rather than the
        // block at the exact foot boundary.
        var bx = (int)MathF.Floor(base.Position.X);
        var by = (int)MathF.Floor(base.Position.Y - 0.05f);
        var bz = (int)MathF.Floor(base.Position.Z);

        var below = world.GetBlock(bx, by, bz);
        GameContext.Current.AudioManager.PlayBlockContactSound(BlockRegistry.GetBlockBreakMaterial(below));
        BlockRegistry.Get(below).OnEntityWalking(world, bx, by, bz, GameContext.Current.GameRandom);
    }

    public void HandleMouseLook(Vector2 delta) => Camera.Rotate(delta.X, delta.Y);

    /// <summary>Teleports the player back to their world spawn point and resets velocity/rotation/ground state (used on death/respawn).</summary>
    public void ResetPosition()
    {
        Position = mSpawnPosition; // the Player.Position setter also collapses render interpolation
        Velocity = Vector3.Zero;
        IsOnGround = false;
        mFallDistance = 0f;
        mWalkDistance = 0f;
        mNextStepDistance = 1;
        Camera.SetRotation(0, -90);
    }

    /// <summary>
    /// Called once when the player transitions from falling to grounded (see TrackFallAndSteps).
    /// Plays a landing sound based on the block underfoot and applies fall damage for falls over
    /// 3 blocks (damage = ceil(distance - 3), so a 4-block fall deals 1 damage).
    /// </summary>
    protected override void Fall(World world, float dist)
    {
        if (dist < 1f)
            return;

        // Sample slightly below the feet (Y - 0.2) to land on the block just walked onto.
        var bx = (int)MathF.Floor(base.Position.X);
        var by = (int)MathF.Floor(base.Position.Y - 0.2f);
        var bz = (int)MathF.Floor(base.Position.Z);
        var mat = BlockRegistry.GetBlockBreakMaterial(world.GetBlock(bx, by, bz));
        GameContext.Current.AudioManager.PlayLandingSound(mat);

        int damage = (int)MathF.Ceiling(dist - 3f);
        if (damage > 0)
            TakeDamage(damage);
    }

    /// <summary>
    /// Applies damage to the player, respecting creative-mode invulnerability and post-hit
    /// invincibility frames, and reducing damage via equipped armor (Minecraft-style formula:
    /// each armor point reduces damage by 4%, i.e. damage * (25 - armorValue) / 25, with the
    /// fractional remainder carried over to the next hit via mDamageRemainder so repeated small
    /// hits aren't rounded away to zero forever).
    /// </summary>
    public override void TakeDamage(int amount)
    {
        // On a server every hit arrives as UpdateHealth. Computing it here as well would leave the
        // two disagreeing, and the HUD flickering between them.
        if (ServerOwnsHealth)
            return;

        if (DamageHandler != null)
        {
            DamageHandler(amount);
            return;
        }

        if (GameContext.Current.IsCreative)
            return;

        if (mInvincibilityTimer > 0)
            return;


        var inv = GameContext.Current.PlayerInventory;
        if (inv != null)
        {
            int armorValue = inv.GetArmorValue();
            // Integer math with a carried remainder: scaledDamage/25 gives whole damage, and the
            // remainder is kept in mDamageRemainder so damage isn't silently lost to rounding
            // when armor reduces a hit below 1 whole point.
            int scaledDamage = amount * (25 - armorValue) + mDamageRemainder;
            int actualDamage = scaledDamage / 25;
            mDamageRemainder = scaledDamage % 25;

            inv.DamageArmor(amount);

            if (actualDamage == 0)
            {
                // Armor fully absorbed this hit (this time) — still grant i-frames so the player
                // isn't hit again instantly, but skip health loss and effects.
                mInvincibilityTimer = INVINCIBILITY_TIMER;
                return;
            }

            amount = actualDamage;
        }

        Health = Math.Max(0, Health - amount);
        mInvincibilityTimer = INVINCIBILITY_TIMER;
        Camera.Shake(0.4f);

        GameContext.Current.AudioManager.PlayPlayerHurtSound();
    }

    /// <summary>Restores health up to PLAYER_MAX_HEALTH (does not grant invincibility frames).</summary>
    public void Heal(int amount)
    {
        this.Health = Math.Min(PLAYER_MAX_HEALTH, Health + amount);
    }
}
