// A block lying on the ground. Bounces, bobs, spins, and is picked up by the player. | DA | 3/2/26
using OpenTK.Graphics.OpenGL4;
using OpenTK.Mathematics;
using VoxelEngine.Core;
using VoxelEngine.Items;
using VoxelEngine.Rendering;
using VoxelEngine.Terrain;
using VoxelEngine.Terrain.Blocks;

namespace VoxelEngine.GameEntity;

public class DroppedItemEntity : Entity
{
    public override bool IsTargetable => false;

    private const float ITEM_GRAVITY = 8f;
    private const float TERMINAL_VEL = 20f;
    private const float DRAG = 0.98f;
    private const float GROUND_FRICTION = 0.7f;
    private const float BOUNCE = 0.4f;
    private const float PICKUP_RADIUS = 3f;
    private const int MAX_AGE = 6000;
    private const int PICKUP_DELAY = 10;
    private const float SPIN_SPEED = 1.2f;

    private const int VERTEX_STRIDE = 8;

    private readonly ItemStack mStack;
    public ItemStack Stack => mStack;
    private readonly bool mIsCubeBlock;
    // World atlas for blocks, items atlas for item drops.
    private readonly Texture mRenderAtlas;
    private int mVao, mVbo;
    private int mVertexCount;

    private float mSpinAngle;
    private readonly float mBobPhase;
    private int mAge;
    private int mPickupDelay = PICKUP_DELAY;

    public DroppedItemEntity(Vector3 position, ItemStack stack, Texture worldAtlas)
    {
        Position = position;
        mStack = stack;
        Width = 0.25f;
        Height = 0.25f;

        var rng = Game.Instance.GameRandom;
        Velocity = new Vector3(
            (float)(rng.NextDouble() - 0.5) * 1.0f,
            3.0f,
            (float)(rng.NextDouble() - 0.5) * 1.0f
        );

        mBobPhase = (float)(rng.NextDouble() * MathF.PI * 2f);

        if (stack.IsBlock)
        {
            var renderType = BlockRegistry.GetRenderType(stack.Block);
            mIsCubeBlock = renderType == RenderingType.Normal
                        || renderType == RenderingType.Slab
                        || renderType == RenderingType.Stair;
        }

        mRenderAtlas = (mIsCubeBlock || stack.IsBlock) ? worldAtlas : Game.Instance.ItemTexture;

        if (mIsCubeBlock)
            BuildCubeMesh();
        else
            BuildBillboardMesh();
    }

    private void BuildCubeMesh()
    {
        var block = BlockRegistry.Get(mStack.Block);
        var verts = new List<float>();

        if (block.RenderType == RenderingType.Stair)
        {
            // Lower half (full width, back half)
            AddBox(verts, block,
                new Vector3(0, 0, 0), new Vector3(1, 0.5f, 1));
            // Upper half (full width, front half)
            AddBox(verts, block,
                new Vector3(0, 0.5f, 0), new Vector3(1, 1f, 0.5f));
        }
        else
        {
            AddBox(verts, block, block.BoundsMin, block.BoundsMax);
        }

        UploadMesh(verts.ToArray());
    }

    private static void AddBox(List<float> verts, Block block, Vector3 min, Vector3 max)
    {
        var top = block.TopTextureCoords;
        var bot = block.BottomTextureCoords;
        var front = block.FrontTextureCoords;
        var back = block.BackTextureCoords;
        var right = block.RightTextureCoords;
        var left = block.LeftTextureCoords;

        float t_u0 = top.TopLeft.X, t_v0 = top.TopLeft.Y, t_u1 = top.BottomRight.X, t_v1 = top.BottomRight.Y;
        float b_u0 = bot.TopLeft.X, b_v0 = bot.TopLeft.Y, b_u1 = bot.BottomRight.X, b_v1 = bot.BottomRight.Y;
        float fr_u0 = front.TopLeft.X, fr_v0 = front.TopLeft.Y, fr_u1 = front.BottomRight.X, fr_v1 = front.BottomRight.Y;
        float bk_u0 = back.TopLeft.X, bk_v0 = back.TopLeft.Y, bk_u1 = back.BottomRight.X, bk_v1 = back.BottomRight.Y;
        float r_u0 = right.TopLeft.X, r_v0 = right.TopLeft.Y, r_u1 = right.BottomRight.X, r_v1 = right.BottomRight.Y;
        float l_u0 = left.TopLeft.X, l_v0 = left.TopLeft.Y, l_u1 = left.BottomRight.X, l_v1 = left.BottomRight.Y;

        float x0 = min.X, x1 = max.X;
        float y0 = min.Y, y1 = max.Y;
        float z0 = min.Z, z1 = max.Z;

        // Front (+Z)
        V(verts, x0, y0, z1, fr_u0, fr_v0, 0, 0, 1); V(verts, x1, y1, z1, fr_u1, fr_v1, 0, 0, 1); V(verts, x0, y1, z1, fr_u0, fr_v1, 0, 0, 1);
        V(verts, x0, y0, z1, fr_u0, fr_v0, 0, 0, 1); V(verts, x1, y0, z1, fr_u1, fr_v0, 0, 0, 1); V(verts, x1, y1, z1, fr_u1, fr_v1, 0, 0, 1);

        // Back (-Z)
        V(verts, x0, y0, z0, bk_u1, bk_v0, 0, 0, -1); V(verts, x0, y1, z0, bk_u1, bk_v1, 0, 0, -1); V(verts, x1, y1, z0, bk_u0, bk_v1, 0, 0, -1);
        V(verts, x0, y0, z0, bk_u1, bk_v0, 0, 0, -1); V(verts, x1, y1, z0, bk_u0, bk_v1, 0, 0, -1); V(verts, x1, y0, z0, bk_u0, bk_v0, 0, 0, -1);

        // Top (+Y)
        V(verts, x0, y1, z0, t_u0, t_v0, 0, 1, 0); V(verts, x1, y1, z1, t_u1, t_v1, 0, 1, 0); V(verts, x1, y1, z0, t_u1, t_v0, 0, 1, 0);
        V(verts, x0, y1, z0, t_u0, t_v0, 0, 1, 0); V(verts, x0, y1, z1, t_u0, t_v1, 0, 1, 0); V(verts, x1, y1, z1, t_u1, t_v1, 0, 1, 0);

        // Bottom (-Y)
        V(verts, x0, y0, z0, b_u0, b_v1, 0, -1, 0); V(verts, x1, y0, z0, b_u1, b_v1, 0, -1, 0); V(verts, x1, y0, z1, b_u1, b_v0, 0, -1, 0);
        V(verts, x0, y0, z0, b_u0, b_v1, 0, -1, 0); V(verts, x1, y0, z1, b_u1, b_v0, 0, -1, 0); V(verts, x0, y0, z1, b_u0, b_v0, 0, -1, 0);

        // Right (+X)
        V(verts, x1, y0, z0, r_u1, r_v0, 1, 0, 0); V(verts, x1, y1, z0, r_u1, r_v1, 1, 0, 0); V(verts, x1, y1, z1, r_u0, r_v1, 1, 0, 0);
        V(verts, x1, y0, z0, r_u1, r_v0, 1, 0, 0); V(verts, x1, y1, z1, r_u0, r_v1, 1, 0, 0); V(verts, x1, y0, z1, r_u0, r_v0, 1, 0, 0);

        // Left (-X)
        V(verts, x0, y0, z1, l_u1, l_v0, -1, 0, 0); V(verts, x0, y1, z1, l_u1, l_v1, -1, 0, 0); V(verts, x0, y1, z0, l_u0, l_v1, -1, 0, 0);
        V(verts, x0, y0, z1, l_u1, l_v0, -1, 0, 0); V(verts, x0, y1, z0, l_u0, l_v1, -1, 0, 0); V(verts, x0, y0, z0, l_u0, l_v0, -1, 0, 0);
    }

    private void BuildBillboardMesh()
    {
        // Item drops from the items texture atlas; block cross/billboard drops from the world atlas.
        var uv = mStack.IsBlock
            ? BlockRegistry.Get(mStack.Block).InventoryTextureCoords
            : ItemRegistry.GetItemCoords(mStack.Item);
        float u0 = uv.TopLeft.X, v0 = uv.TopLeft.Y;
        float u1 = uv.BottomRight.X, v1 = uv.BottomRight.Y;

        // A quad centered in X, bottom at Y=0, facing +Z.
        float[] arr =
        {
            -0.25f, 0.0f, 0f,  u0, v0,  0f, 0f, 1f,
             0.25f, 0.0f, 0f,  u1, v0,  0f, 0f, 1f,
             0.25f, 0.5f, 0f,  u1, v1,  0f, 0f, 1f,

            -0.25f, 0.0f, 0f,  u0, v0,  0f, 0f, 1f,
             0.25f, 0.5f, 0f,  u1, v1,  0f, 0f, 1f,
            -0.25f, 0.5f, 0f,  u0, v1,  0f, 0f, 1f,
        };

        UploadMesh(arr);
    }

    private void UploadMesh(float[] arr)
    {
        mVertexCount = arr.Length / VERTEX_STRIDE;
        mVao = GL.GenVertexArray();
        mVbo = GL.GenBuffer();
        GL.BindVertexArray(mVao);
        GL.BindBuffer(BufferTarget.ArrayBuffer, mVbo);
        GL.BufferData(BufferTarget.ArrayBuffer, arr.Length * sizeof(float), arr, BufferUsageHint.StaticDraw);

        int stride = VERTEX_STRIDE * sizeof(float);
        GL.VertexAttribPointer(0, 3, VertexAttribPointerType.Float, false, stride, 0);
        GL.EnableVertexAttribArray(0);
        GL.VertexAttribPointer(1, 2, VertexAttribPointerType.Float, false, stride, 3 * sizeof(float));
        GL.EnableVertexAttribArray(1);
        GL.VertexAttribPointer(2, 3, VertexAttribPointerType.Float, false, stride, 5 * sizeof(float));
        GL.EnableVertexAttribArray(2);
        GL.BindVertexArray(0);
    }

    private static void V(List<float> v, float px, float py, float pz,
                                          float u, float vv,
                                          float nx, float ny, float nz)
    {
        v.Add(px); v.Add(py); v.Add(pz);
        v.Add(u); v.Add(vv);
        v.Add(nx); v.Add(ny); v.Add(nz);
    }

    public override void Tick(World world)
    {
        float dt = TickSystem.TICK_DURATION;

        if (mPickupDelay > 0)
            mPickupDelay--;

        if (++mAge >= MAX_AGE)
        {
            IsAlive = false;
            return;
        }

        mSpinAngle += SPIN_SPEED * dt;

        // Gravity
        float vy = Velocity.Y - ITEM_GRAVITY * dt;
        if (vy < -TERMINAL_VEL) 
            vy = -TERMINAL_VEL;
        
        Velocity = new Vector3(Velocity.X, vy, Velocity.Z);

        // Lava: pop upward and scatter
        int bx = (int)MathF.Floor(Position.X);
        int by = (int)MathF.Floor(Position.Y + Height * 0.5f);
        int bz = (int)MathF.Floor(Position.Z);
        if (world.GetBlock(bx, by, bz) == BlockType.Lava)
        {
            var rng = Game.Instance.GameRandom;
            Velocity = new Vector3(
                (float)(rng.NextDouble() - 0.5) * 4f,
                4.0f,
                (float)(rng.NextDouble() - 0.5) * 4f
            );
        }

        // Move and resolve collisions
        Vector3 frameVel = Velocity * dt;
        Vector3 actual = Physics.MoveWithCollision(world, GetBoundingBox(), frameVel);
        Position += actual;

        bool hitY = MathF.Abs(actual.Y) < MathF.Abs(frameVel.Y) * 0.99f;
        if (hitY)
        {
            if (Velocity.Y < 0)
            {
                IsOnGround = true;
                float bounce = -Velocity.Y * BOUNCE;
                Velocity = new Vector3(Velocity.X, bounce < 0.5f ? 0f : bounce, Velocity.Z);
            }
            else
            {
                Velocity = new Vector3(Velocity.X, 0f, Velocity.Z);
            }
        }
        else
        {
            IsOnGround = Physics.IsOnGround(world, GetBoundingBox());
        }

        Velocity *= DRAG;

        if (IsOnGround)
            Velocity = new Vector3(Velocity.X * GROUND_FRICTION, Velocity.Y, Velocity.Z * GROUND_FRICTION);

        if (mPickupDelay == 0)
        {
            float dist = (Game.Instance.GetPlayer.Position - Position).Length;
            var inv = Game.Instance.PlayerInventory;
            if (dist < PICKUP_RADIUS && inv != null && inv.TryAdd(mStack))
            {
                Game.Instance.AudioManager.PlayPickupSound();
                IsAlive = false;
            }

        }
    }

    protected override void DrawModel(Matrix4 view, Matrix4 projection)
    {
        float bob = MathF.Sin(mAge / 10.0f + mBobPhase) * 0.1f + 0.1f;

        if (mIsCubeBlock)
            DrawCube(view, projection, bob);
        else
            DrawBillboard(view, projection, bob);
    }

    private void DrawCube(Matrix4 view, Matrix4 projection, float bob)
    {
        // Center the [0..1]³ cube in XZ, scale to 0.25, spin, then translate to world pos.
        Matrix4 mvp =
            Matrix4.CreateTranslation(-0.5f, 0f, -0.5f)
            * Matrix4.CreateScale(0.25f)
            * Matrix4.CreateRotationY(mSpinAngle)
            * Matrix4.CreateTranslation(Position + new Vector3(0f, bob, 0f))
            * view
            * projection;

        _shader?.SetMatrix4("mvp", mvp);
        mRenderAtlas.Use(TextureUnit.Texture0);
        GL.BindVertexArray(mVao);
        GL.DrawArrays(PrimitiveType.Triangles, 0, mVertexCount);
    }

    private void DrawBillboard(Matrix4 view, Matrix4 projection, float bob)
    {
        // Horizontal billboard: rotate the quad so its +Z normal faces the camera.
        float dx = Game.Instance.GetPlayer.Camera.Position.X - Position.X;
        float dz = Game.Instance.GetPlayer.Camera.Position.Z - Position.Z;

        Matrix4 mvp =
            Matrix4.CreateRotationY(MathF.Atan2(dx, dz))
            * Matrix4.CreateTranslation(Position + new Vector3(0f, bob, 0f))
            * view
            * projection;

        _shader?.SetMatrix4("mvp", mvp);
        mRenderAtlas.Use(TextureUnit.Texture0);
        GL.BindVertexArray(mVao);
        GL.DrawArrays(PrimitiveType.Triangles, 0, mVertexCount);
    }

    public override void Dispose()
    {
        GL.DeleteVertexArray(mVao);
        GL.DeleteBuffer(mVbo);
    }
}
