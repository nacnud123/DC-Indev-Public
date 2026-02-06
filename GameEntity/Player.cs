// Main player class. Has functions for movement and block interaction | DA | 2/5/26
using OpenTK.Mathematics;
using OpenTK.Windowing.GraphicsLibraryFramework;
using VoxelEngine.Core;
using VoxelEngine.Terrain;
using VoxelEngine.Terrain.Blocks;

namespace VoxelEngine.GameEntity;
public class Player : Entity
{
    private const float SPRINT_MULTIPLIER = 5f;
    private const float FLY_SPEED = 10.0f;
    private const float FLY_SPRINT_MULTIPLIER = 10f;

    public Camera Camera { get; }
    public bool IsFlying { get; private set; }
    public bool IsSprinting { get; private set; }

    private Vector3 mSpawnPosition;
    private BlockType mSelectedBlock = BlockType.Grass;

    public new Vector3 Position
    {
        get => base.Position;
        set
        {
            base.Position = value;
            Camera.Position = value + new Vector3(0, EyeHeight, 0);
        }
    }

    public Player(Vector3 spawnPosition, float aspectRatio)
    {
        mSpawnPosition = spawnPosition;
        base.Position = spawnPosition;
        Camera = new Camera(spawnPosition + new Vector3(0, EyeHeight, 0), aspectRatio);
    }

    public override void Tick(World world)
    {
        // Does nothing in player
    }

    public void Update(World world, KeyboardState keyboard, float deltaTime)
    {
        HandleInput(keyboard);

        if (IsFlying)
            UpdateFlying(world, keyboard, deltaTime);
        else
            UpdateSurvival(world, keyboard, deltaTime);
    }

    private void HandleInput(KeyboardState keyboard)
    {
        // Fly mode
        if (keyboard.IsKeyPressed(Keys.F))
        {
            IsFlying = !IsFlying;
            if (IsFlying) Velocity = new Vector3(Velocity.X, 0, Velocity.Z);
        }

        // Sprint
        IsSprinting = keyboard.IsKeyDown(Keys.LeftShift);

        // Blocks
        if (keyboard.IsKeyPressed(Keys.D1)) mSelectedBlock = BlockType.Grass;
        if (keyboard.IsKeyPressed(Keys.D2)) mSelectedBlock = BlockType.Dirt;
        if (keyboard.IsKeyPressed(Keys.D3)) mSelectedBlock = BlockType.Stone;
        if (keyboard.IsKeyPressed(Keys.D4)) mSelectedBlock = BlockType.Wood;
        if (keyboard.IsKeyPressed(Keys.D5)) mSelectedBlock = BlockType.Leaves;
        if (keyboard.IsKeyPressed(Keys.D6)) mSelectedBlock = BlockType.Sand;
        if (keyboard.IsKeyPressed(Keys.D7)) mSelectedBlock = BlockType.Glowstone;
        if (keyboard.IsKeyPressed(Keys.D8)) mSelectedBlock = BlockType.Glass;
        if (keyboard.IsKeyPressed(Keys.D9)) mSelectedBlock = BlockType.Torch;
        if (keyboard.IsKeyPressed(Keys.D0)) mSelectedBlock = BlockType.Flower;
    }

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

    private void UpdateSurvival(World world, KeyboardState keyboard, float deltaTime)
    {
        // Gravity
        var vel = Velocity;
        vel.Y -= Physics.GRAVITY * deltaTime;
        vel.Y = MathF.Max(vel.Y, -Physics.TERMINAL_VEL);
        Velocity = vel;

        // Jump
        if (keyboard.IsKeyPressed(Keys.Space) && IsOnGround)
        {
            vel.Y = Physics.JUMP_VEL;
            Velocity = vel;
            IsOnGround = false;
        }

        // Horizontal movement
        float speed = IsSprinting ? WalkSpeed * SPRINT_MULTIPLIER : WalkSpeed;
        Vector3 moveDir = GetMoveDirection(keyboard);

        Vector3 frameVelocity = new Vector3(
            moveDir.X * speed,
            Velocity.Y,
            moveDir.Z * speed
        ) * deltaTime;

        Vector3 actual = Physics.MoveWithCollision(world, GetBoundingBox(), frameVelocity);
        Position += actual;

        // Ground detection
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

    public void HandleMouseLook(Vector2 delta) => Camera.Rotate(delta.X, delta.Y);

    public void HandleBlockInteraction(World world, bool breakBlock, bool placeBlock)
    {
        var hit = world.Raycast(Camera.Position, Camera.Front);
        if (hit.Type != RaycastHitType.Block)
            return;

        var blockPos = hit.BlockPos;
        var placePos = hit.PlacePos;

        if (breakBlock)
        {
            var tempBlock = world.GetBlock(blockPos.X, blockPos.Y, blockPos.Z);

            if (!BlockRegistry.IsBreakable(tempBlock))
                return;

            world.SetBlock(blockPos.X, blockPos.Y, blockPos.Z, BlockType.Air);

            Game.Instance.ParticleSystem.SpawnBlockBreakParticles(blockPos, tempBlock);

            var checkY = blockPos.Y + 1;

            // Gravity blocks
            while (BlockRegistry.IsGravityBlock(world.GetBlock(blockPos.X, checkY, blockPos.Z)))
            {
                world.SetBlock(blockPos.X, checkY - 1, blockPos.Z, world.GetBlock(blockPos.X, checkY, blockPos.Z));
                world.SetBlock(blockPos.X, checkY, blockPos.Z, BlockType.Air);
                checkY += 1;
            }
        }
        else if (placeBlock && placePos.HasValue)
        {
            Aabb blockBox = Aabb.BlockAabb(placePos.Value.X, placePos.Value.Y, placePos.Value.Z);
            if (!GetBoundingBox().Intersects(blockBox))
            {
                int x = placePos.Value.X, y = placePos.Value.Y, z = placePos.Value.Z;

                // Gravity blocks
                if (BlockRegistry.IsGravityBlock(mSelectedBlock))
                {
                    while (y > 0 && world.GetBlock(x, y - 1, z) == BlockType.Air)
                    {
                        y--;
                    }
                }

                if (world.GetBlock(x, y, z) != BlockType.Air)
                    return;

                if (world.GetBlock(x, y - 1, z) == BlockType.Grass && !BlockRegistry.GetSuffocatesBeneath(mSelectedBlock))
                {
                    world.SetBlock(x, y - 1, z, BlockType.Dirt);
                }

                world.SetBlock(x, y, z, mSelectedBlock);
            }
        }
    }

    public void ResetPosition()
    {
        Position = mSpawnPosition;
        Velocity = Vector3.Zero;
        IsOnGround = false;
        Camera.SetRotation(0, -90);
    }

    public BlockType SelectedBlock
    {
        get => mSelectedBlock;
        set => mSelectedBlock = value;
    }
}
