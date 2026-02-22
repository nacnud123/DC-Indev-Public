// Main player function, handles stuff like movement, block selection, walking audio, block interaction management | DA | 2/14/26
using OpenTK.Mathematics;
using OpenTK.Windowing.GraphicsLibraryFramework;
using VoxelEngine.Core;
using VoxelEngine.Terrain;
using VoxelEngine.Terrain.Blocks;

namespace VoxelEngine.GameEntity;

public partial class Player : Entity
{
    private const float SPRINT_MULTIPLIER = 5f;
    private const float FLY_SPEED = 10.0f;
    private const float FLY_SPRINT_MULTIPLIER = 10f;

    // Swimming
    private const float SWIM_SPEED = 2.5f;
    private const float SWIM_UP_SPEED = 3.5f;
    private const float SWIM_SPRINT_MULT = 2.0f;
    private const float WATER_GRAVITY = 4.0f;
    private const float WATER_DRAG = 0.8f;
    private const float WATER_TERMINAL = 4.0f;
    private const float WATER_JUMP_BOOST = 6.0f;

    public Camera Camera { get; }
    public bool IsFlying { get; private set; }
    public bool IsSprinting { get; private set; }
    public bool IsUnderWater { get; private set; }
    public bool IsInWater { get; private set; }
    public bool IsUnderLava { get; private set; }
    public bool IsInLava { get; private set; }
    private bool IsSlowedDown { get; set; }
    private bool mWasInWater = false;
    private bool mWasInLava = false;
    
    public float HorizontalSpeed { get; private set; }

    private float mStepTimer = 0;
    private float mStepInterval = .5f;

    private Vector3 mSpawnPosition;
    private BlockType mSelectedBlock = BlockType.Grass;
    private float mSmoothEyeY;

    private const float EYE_SMOOTH_SPEED = 16f;

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
        mSpawnPosition = spawnPosition;
        base.Position = spawnPosition;
        mSmoothEyeY = spawnPosition.Y + EyeHeight;
        Camera = new Camera(spawnPosition + new Vector3(0, EyeHeight, 0), aspectRatio);
        IsSlowedDown = false;
    }

    public override void Tick(World world)
    {
    }

    public void Update(World world, KeyboardState keyboard, float deltaTime)
    {
        HandleInput(keyboard);
        UpdateUnderwaterState(world);

        if (IsInWater && !mWasInWater)
        {
            Game.Instance.ParticleSystem.SpawnBlockBreakParticles(
                new Vector3i((int)MathF.Floor(Position.X), (int)MathF.Floor(Position.Y + 1), (int)MathF.Floor(Position.Z)),
                BlockType.Water
            );

            Game.Instance.AudioManager.PlayBlockContactSound(BlockBreakMaterial.Water);
        }

        if (IsInLava && !mWasInLava)
        {
            Game.Instance.ParticleSystem.SpawnBlockBreakParticles(
                new Vector3i((int)MathF.Floor(Position.X), (int)MathF.Floor(Position.Y + 1), (int)MathF.Floor(Position.Z)),
                BlockType.Lava
            );
        }

        mWasInWater = IsInWater;
        mWasInLava = IsInLava;

        if (IsFlying)
            UpdateFlying(world, keyboard, deltaTime);
        else if(IsInWater || IsInLava)
            UpdateSwimming(world, keyboard, deltaTime);
        else
            UpdateSurvival(world, keyboard, deltaTime);

        // Smoothly interpolate camera Y to avoid jarring snaps on step-up/down
        float targetEyeY = base.Position.Y + EyeHeight;
        mSmoothEyeY = mSmoothEyeY + (targetEyeY - mSmoothEyeY) * MathF.Min(1f, EYE_SMOOTH_SPEED * deltaTime);
        Camera.Position = new Vector3(base.Position.X, mSmoothEyeY, base.Position.Z);
    }

    // This function checks if the player is underwater or is in a block that slows them down
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

    // Keyboard management
    private void HandleInput(KeyboardState keyboard)
    {
        if (keyboard.IsKeyPressed(Keys.F))
        {
            IsFlying = !IsFlying;
            if (IsFlying) Velocity = new Vector3(Velocity.X, 0, Velocity.Z);
        }

        IsSprinting = keyboard.IsKeyDown(Keys.LeftShift);
    }

    // Do special movement when the player is flying
    private void UpdateFlying(World world, KeyboardState keyboard, float deltaTime)
    {
        float speed = IsSprinting ? FLY_SPEED * FLY_SPRINT_MULTIPLIER : FLY_SPEED;
        Vector3 moveDir = GetMoveDirection(keyboard);
        Vector3 movement = moveDir * speed * deltaTime;

        if (keyboard.IsKeyDown(Keys.Space))
            movement.Y += speed * deltaTime;

        if (keyboard.IsKeyDown(Keys.LeftControl))
            movement.Y -= speed * deltaTime;

        Vector3 actual = Physics.MoveWithCollision(world, GetBoundingBox(), movement);
        Position += actual;
        IsOnGround = Physics.IsOnGround(world, GetBoundingBox());
    }

    // Movement for when the player is not flying
    private void UpdateSurvival(World world, KeyboardState keyboard, float deltaTime)
    {
        var vel = Velocity;

        float grav = IsSlowedDown ? Physics.GRAVITY * 0.15f : Physics.GRAVITY;
        float termVel = IsSlowedDown ? 3f : Physics.TERMINAL_VEL;

        if (IsSlowedDown && vel.Y < -termVel)
            vel.Y = -termVel;

        vel.Y -= grav * deltaTime;
        vel.Y = MathF.Max(vel.Y, -termVel);
        Velocity = vel;

        if (keyboard.IsKeyPressed(Keys.Space) && IsOnGround)
        {
            vel.Y = IsSlowedDown ? 3f : Physics.JUMP_VEL;
            Velocity = vel;
            IsOnGround = false;
        }

        float speed = IsSprinting ? WalkSpeed * SPRINT_MULTIPLIER : WalkSpeed;
        if (IsSlowedDown)
            speed *= 0.4f;

        Vector3 moveDir = GetMoveDirection(keyboard);

        Vector3 frameVelocity = new Vector3(
            moveDir.X * speed,
            Velocity.Y,
            moveDir.Z * speed
        ) * deltaTime;

        float step = IsOnGround ? Physics.STEP_HEIGHT : 0f;
        Vector3 actual = Physics.MoveWithCollision(world, GetBoundingBox(), frameVelocity, step);
        Position += actual;

        float horizontalSpeed = MathF.Sqrt(actual.X * actual.X + actual.Z * actual.Z) / deltaTime;
        HorizontalSpeed = horizontalSpeed;

        if (IsOnGround && horizontalSpeed > 0.2f)
        {
            mStepTimer -= deltaTime;

            if (mStepTimer <= 0)
            {
                mStepTimer = mStepInterval;

                var blockBelowX = (int)MathF.Floor(Position.X);
                var blockBelowY = (int)MathF.Floor(Position.Y - 0.05f);
                var blockBelowZ = (int)MathF.Floor(Position.Z);

                var belowBlockType = world.GetBlock(blockBelowX, blockBelowY, blockBelowZ);
                var blockBelowMat = BlockRegistry.GetBlockBreakMaterial(belowBlockType);
                Game.Instance.AudioManager.PlayBlockContactSound(blockBelowMat);
            }
        }
        else
        {
            mStepTimer = 0;
        }

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
    }

    // Get the movement direction based on keyboard inputs
    private Vector3 GetMoveDirection(KeyboardState keyboard)
    {
        Vector3 forward = Camera.Front;
        forward.Y = 0;

        if (forward.LengthSquared > 0.001f)
            forward.Normalize();

        Vector3 right = Camera.Right;
        right.Y = 0;

        if (right.LengthSquared > 0.001f)
            right.Normalize();

        Vector3 dir = Vector3.Zero;

        if (keyboard.IsKeyDown(Keys.W))
            dir += forward;

        if (keyboard.IsKeyDown(Keys.S))
            dir -= forward;

        if (keyboard.IsKeyDown(Keys.A))
            dir -= right;

        if (keyboard.IsKeyDown(Keys.D))
            dir += right;

        return dir.LengthSquared > 0 ? dir.Normalized() : dir;
    }

    // Do swimming movement
    private void UpdateSwimming(World world, KeyboardState keyboard, float deltaTime)
    {
        var vel = Velocity;
        vel.Y -= WATER_GRAVITY * deltaTime;
        vel.Y = Math.Max(vel.Y, -WATER_TERMINAL);

        if (keyboard.IsKeyDown(Keys.Space))
        {

            var headX = (int)MathF.Floor(Position.X);
            var headY = (int)MathF.Floor(Position.Y);
            var headZ = (int)MathF.Floor(Position.Z);
            var headBlock = world.GetBlock(headX, headY, headZ);

            if (headBlock != BlockType.Water)
                vel.Y = WATER_JUMP_BOOST;
            else
                vel.Y = SWIM_UP_SPEED;
        }


        if (keyboard.IsKeyDown(Keys.LeftControl))
            vel.Y = -SWIM_UP_SPEED;

        vel.X *= MathF.Pow(WATER_DRAG, deltaTime * 20);
        vel.Y *= MathF.Pow(WATER_DRAG, deltaTime * 20);
        vel.Z *= MathF.Pow(WATER_DRAG, deltaTime * 20);

        Velocity = vel;

        var speed = IsSprinting ? SWIM_SPEED * SWIM_SPRINT_MULT : SWIM_SPEED;
        var moveDir = GetMoveDirection(keyboard);

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

    // Reset the player back to spawn point
    public void ResetPosition()
    {
        base.Position = mSpawnPosition;
        mSmoothEyeY = mSpawnPosition.Y + EyeHeight;
        Camera.Position = new Vector3(mSpawnPosition.X, mSmoothEyeY, mSpawnPosition.Z);
        Velocity = Vector3.Zero;
        IsOnGround = false;
        Camera.SetRotation(0, -90);
    }
}
