// Projectile fired by the bow. Travels with gravity, embeds in blocks, damages entities, and can be picked up. | DA | 2/28/26

using OpenTK.Graphics.OpenGL4;
using OpenTK.Mathematics;
using VoxelEngine.Core;
using VoxelEngine.Items;
using VoxelEngine.Rendering;
using VoxelEngine.Terrain;

namespace VoxelEngine.GameEntity;

public class ArrowEntity : Entity
{
    public override bool IsTargetable => false;
    public override float ShadowSize => 0f;

    public const float SPEED = 0.6f;
    private const float SPREAD = 0.0075f;
    private const float DRAG_AIR = 0.99f;
    private const float DRAG_WATER = 0.80f;
    private const float GRAVITY = 0.02f;
    private const int ARROW_DAMAGE = 4;
    private const int DESPAWN_TICKS = 1200;
    private const int SHAKE_TICKS = 7;
    private const int OWNER_GRACE = 5;
    private const float PICKUP_RADIUS = 1.5f;

    private const string TEXTURE_PATH = "Resources/arrows.png";

    // Shared mesh — built once, reused by all arrows.
    private static int sVao;
    private static int sVbo;
    private static int sVertexCount;
    private static bool sMeshReady;
    private static Texture? sTexture;

    private readonly Entity mOwner;
    private bool mInGround;
    private Vector3i mStuckBlock;
    private BlockType mStuckBlockType;
    private int mTicksInGround;
    private int mTicksInAir;
    private int mArrowShake;
    private Vector3 mFacing;

    public ArrowEntity(Entity owner)
    {
        mOwner = owner;
        Width = 0.5f;
        Height = 0.5f;

        EnsureMesh();

        var player = owner as Player;

        float yawDeg = player != null ? player.Camera.Yaw : MathHelper.RadiansToDegrees(owner.Yaw);
        float pitchDeg = player != null ? player.Camera.Pitch : 0f;
        float yawRad = MathHelper.DegreesToRadians(yawDeg);
        float pitchRad = MathHelper.DegreesToRadians(pitchDeg);

        Position = player != null ? player.Camera.Position : owner.Position;

        float dirX = MathF.Cos(pitchRad) * MathF.Cos(yawRad);
        float dirY = MathF.Sin(pitchRad);
        float dirZ = MathF.Cos(pitchRad) * MathF.Sin(yawRad);

        Position += new Vector3(dirX * 0.3f, dirY * 0.3f, dirZ * 0.3f);

        SetHeading(dirX, dirY, dirZ);
    }

    // Non-player owners: spawn at origin, fire in the given normalized direction. spread overrides the default SPREAD value (e.g. skeletons use ~12x player spread).
    public ArrowEntity(Entity owner, Vector3 origin, Vector3 direction, float spread = SPREAD)
    {
        mOwner = owner;
        Width = 0.5f;
        Height = 0.5f;

        EnsureMesh();

        Position = origin + direction * 0.3f;
        SetHeading(direction.X, direction.Y, direction.Z, spread);
    }

    private static void EnsureMesh()
    {
        if (sMeshReady)
            return;

        sTexture = Texture.LoadFromFile(TEXTURE_PATH);

        const float SU0 = 0f, SU1 = 16f / 32f;
        const float SV0 = 1f, SV1 = 1f - 5f / 32f;
        var v = new List<float>();

        // Two quads crossed at 90°, each split into front and back faces offset by ±O. The tiny separation prevents Z-fighting where the quads share depth planes.
        const float O = 0.01f;

        // Quad 1: lies in XY plane, faces along Z.
        Quad(v, -8, -2, -O, SU0, SV0, 8, -2, -O, SU1, SV0, 8, 2, -O, SU1, SV1, -8, 2, -O, SU0, SV1, 0, 0, 1);
        Quad(v, -8, 2, O, SU0, SV0, 8, 2, O, SU1, SV0, 8, -2, O, SU1, SV1, -8, -2, O, SU0, SV1, 0, 0, -1);

        // Quad 2: lies in XZ plane, faces along Y.
        Quad(v, -8, O, -2, SU0, SV0, 8, O, -2, SU1, SV0, 8, O, 2, SU1, SV1, -8, O, 2, SU0, SV1, 0, 1, 0);
        Quad(v, -8, -O, 2, SU0, SV0, 8, -O, 2, SU1, SV0, 8, -O, -2, SU1, SV1, -8, -O, -2, SU0, SV1, 0, -1, 0);

        // Back cap: small square at the tail, faces along -X.
        const float CU0 = 0f, CU1 = 5f / 32f;
        const float CV0 = 1f - 5f / 32f, CV1 = 1f - 10f / 32f;
        Quad(v, -5, -2, -2, CU0, CV0, -5, -2, 2, CU1, CV0, -5, 2, 2, CU1, CV1, -5, 2, -2, CU0, CV1, -1, 0, 0);

        float[] arr = v.ToArray();
        sVertexCount = arr.Length / 8;

        sVao = GL.GenVertexArray();
        sVbo = GL.GenBuffer();
        GL.BindVertexArray(sVao);
        GL.BindBuffer(BufferTarget.ArrayBuffer, sVbo);
        GL.BufferData(BufferTarget.ArrayBuffer, arr.Length * sizeof(float), arr, BufferUsageHint.StaticDraw);

        int stride = 8 * sizeof(float);
        GL.VertexAttribPointer(0, 3, VertexAttribPointerType.Float, false, stride, 0);
        GL.EnableVertexAttribArray(0);
        GL.VertexAttribPointer(1, 2, VertexAttribPointerType.Float, false, stride, 3 * sizeof(float));
        GL.EnableVertexAttribArray(1);
        GL.VertexAttribPointer(2, 3, VertexAttribPointerType.Float, false, stride, 5 * sizeof(float));
        GL.EnableVertexAttribArray(2);
        GL.BindVertexArray(0);

        sMeshReady = true;
    }

    private static void Quad(List<float> v,
        float x0, float y0, float z0, float u0, float w0,
        float x1, float y1, float z1, float u1, float w1,
        float x2, float y2, float z2, float u2, float w2,
        float x3, float y3, float z3, float u3, float w3,
        float nx, float ny, float nz)
    {
        V(v, x0, y0, z0, u0, w0, nx, ny, nz);
        V(v, x1, y1, z1, u1, w1, nx, ny, nz);
        V(v, x2, y2, z2, u2, w2, nx, ny, nz);
        V(v, x0, y0, z0, u0, w0, nx, ny, nz);
        V(v, x2, y2, z2, u2, w2, nx, ny, nz);
        V(v, x3, y3, z3, u3, w3, nx, ny, nz);
    }

    private static void V(List<float> v, float x, float y, float z, float u, float w, float nx, float ny, float nz)
    {
        v.Add(x);
        v.Add(y);
        v.Add(z);
        v.Add(u);
        v.Add(w);
        v.Add(nx);
        v.Add(ny);
        v.Add(nz);
    }

    public static void DisposeMesh()
    {
        if (!sMeshReady)
            return;

        GL.DeleteVertexArray(sVao);
        GL.DeleteBuffer(sVbo);
        sTexture?.Dispose();
        sMeshReady = false;
    }

    private void SetHeading(float vx, float vy, float vz, float spread = SPREAD)
    {
        float len = MathF.Sqrt(vx * vx + vy * vy + vz * vz);
        if (len > 0f)
        {
            vx /= len;
            vy /= len;
            vz /= len;
        }

        var rng = Game.Instance.GameRandom;
        vx += ((float)rng.NextDouble() * 2f - 1f) * spread;
        vy += ((float)rng.NextDouble() * 2f - 1f) * spread;
        vz += ((float)rng.NextDouble() * 2f - 1f) * spread;

        Velocity = new Vector3(vx * SPEED, vy * SPEED, vz * SPEED);
        UpdateOrientation();
    }

    private void UpdateOrientation()
    {
        if (Velocity.LengthSquared > 0f)
            mFacing = Velocity.Normalized();
    }

    public override void Tick(World world)
    {
        if (mArrowShake > 0)
            mArrowShake--;

        if (mInGround)
            TickStuck(world);
        else
            TickFlying(world);
    }

    private void TickStuck(World world)
    {
        if (world.GetBlock(mStuckBlock.X, mStuckBlock.Y, mStuckBlock.Z) == mStuckBlockType)
        {
            mTicksInGround++;
            if (mTicksInGround >= DESPAWN_TICKS)
                IsAlive = false;

            TryPickup();
            return;
        }

        mInGround = false;
        var rng = Game.Instance.GameRandom;
        Velocity = new Vector3(
            Velocity.X * (float)rng.NextDouble() * 0.2f,
            Velocity.Y * (float)rng.NextDouble() * 0.2f,
            Velocity.Z * (float)rng.NextDouble() * 0.2f);
        mTicksInGround = 0;
        mTicksInAir = 0;
    }

    private void TickFlying(World world)
    {
        mTicksInAir++;

        float speed = Velocity.Length;
        Vector3 dir = speed > 0f ? Velocity / speed : Vector3.Zero;

        var blockHit = world.Raycast(Position, dir, speed, solidOnly: true);

        Entity? hitEntity = null;
        float hitEntityDist = float.MaxValue;

        // Check the player first (not stored in world.Entities)
        var player = Game.Instance.GetPlayer;
        if (player != mOwner || mTicksInAir >= OWNER_GRACE)
        {
            if (player.IsTargetable && player.IsAlive
                                    && player.IsLookedAt(Position, dir, speed + 1f, out float playerDist)
                                    && playerDist <= speed + 1f)
            {
                hitEntity = player;
                hitEntityDist = playerDist;
            }
        }

        foreach (var entity in world.Entities)
        {
            if (entity == this)
                continue;

            if (entity == mOwner && mTicksInAir < OWNER_GRACE)
                continue;

            if (!entity.IsTargetable || !entity.IsAlive)
                continue;

            if (entity.IsLookedAt(Position, dir, speed + 1f, out float dist)
                && dist < hitEntityDist && dist <= speed + 1f)
            {
                hitEntity = entity;
                hitEntityDist = dist;
            }
        }

        bool entityCloser = hitEntity != null
                            && (blockHit.Type != RaycastHitType.Block || hitEntityDist < blockHit.Distance);

        if (entityCloser)
        {
            hitEntity!.TakeDamage(ARROW_DAMAGE);
            
            // Knock target back along the arrow's travel direction with a slight upward kick
            Vector3 knockback = new Vector3(dir.X, MathF.Abs(dir.Y) + 0.6f, dir.Z) * 12f;
            hitEntity.Velocity += knockback;
            IsAlive = false;
            return;
        }

        if (blockHit.Type == RaycastHitType.Block)
        {
            mStuckBlock = blockHit.BlockPos;
            mStuckBlockType = world.GetBlock(mStuckBlock.X, mStuckBlock.Y, mStuckBlock.Z);
            Position = Position + dir * blockHit.Distance;
            Velocity = Vector3.Zero;
            mInGround = true;
            mArrowShake = SHAKE_TICKS;
            float dist = (Game.Instance.GetPlayer.Position - Position).Length;
            int vol = Proximity(dist, 20f, Game.Instance.AudioManager.SfxVol);
            Game.Instance.AudioManager.PlayAudio("Resources/Audio/Bow/ArrowHit.ogg", vol);
            return;
        }

        Position += Velocity;

        float drag = world.GetBlock(
            (int)MathF.Floor(Position.X),
            (int)MathF.Floor(Position.Y),
            (int)MathF.Floor(Position.Z)) == BlockType.Water
            ? DRAG_WATER
            : DRAG_AIR;

        Velocity = new Vector3(Velocity.X * drag, Velocity.Y * drag - GRAVITY, Velocity.Z * drag);

        UpdateOrientation();
    }

    private void TryPickup()
    {
        if (mArrowShake > 0)
            return;

        var player = Game.Instance.GetPlayer;
        if ((player.Position - Position).LengthSquared > PICKUP_RADIUS * PICKUP_RADIUS) return;

        var inv = Game.Instance.PlayerInventory;
        if (inv != null && inv.TryAdd(ItemStack.FromItem(ItemType.Arrow, 1)))
        {
            Game.Instance.AudioManager.PlayPickupSound();
            IsAlive = false;
        }
    }

    protected override void DrawModel(Matrix4 view, Matrix4 projection)
    {
        if (!sMeshReady || sTexture == null)
            return;
        
        Vector3 fwd = mFacing.LengthSquared > 0f ? mFacing : Vector3.UnitX;
        Vector3 up = MathF.Abs(Vector3.Dot(fwd, Vector3.UnitY)) > 0.99f ? Vector3.UnitZ : Vector3.UnitY;
        Vector3 right = Vector3.Cross(fwd, up).Normalized();
        up = Vector3.Cross(right, fwd).Normalized();

        var rot = new Matrix4(
            new Vector4(fwd.X, fwd.Y, fwd.Z, 0),
            new Vector4(up.X, up.Y, up.Z, 0),
            new Vector4(right.X, right.Y, right.Z, 0),
            new Vector4(0, 0, 0, 1));

        // Shake
        float shakeDeg = mArrowShake > 0 ? -MathF.Sin(mArrowShake * 3f) * mArrowShake : 0f;

        const float SCALE = 0.05625f;

        Matrix4 mvp =
            Matrix4.CreateScale(SCALE)
            * Matrix4.CreateRotationX(MathHelper.DegreesToRadians(shakeDeg))
            * rot
            * Matrix4.CreateTranslation(Position)
            * view
            * projection;

        _shader?.SetMatrix4("mvp", mvp);
        sTexture.Use(TextureUnit.Texture0);
        GL.Disable(EnableCap.CullFace);
        GL.BindVertexArray(sVao);
        GL.DrawArrays(PrimitiveType.Triangles, 0, sVertexCount);
        GL.Enable(EnableCap.CullFace);
    }

    public override void Dispose()
    {
    }
}