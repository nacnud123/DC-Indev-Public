// TNT block entity: falls with gravity, flashes white on a fuse, then explodes. Kind of like a block entity. | DA | 2/17/26
using OpenTK.Graphics.OpenGL4;
using OpenTK.Mathematics;
using VoxelEngine.Core;
using VoxelEngine.Rendering;
using VoxelEngine.Terrain;
using VoxelEngine.Terrain.Blocks;
using VoxelEngine.Utils;

namespace VoxelEngine.GameEntity;

public class TntEntity : Entity
{
    private const float FUSE_DURATION = 4f;
    private const float FLASH_INTERVAL_START = 0.5f;
    private const float FLASH_INTERVAL_END = 0.1f;
    private const int EXPLOSION_RADIUS = 4;
    private const float EXPLOSION_POWER = 4.0f;
    private const float KNOCKBACK_STRENGTH = 30f;
    private const float FRICTION_COEFFICIENT = 0.85f;

    private const int VERTEX_STRIDE = 8;

    private readonly Texture mWorldAtlas;
    private int mVao, mVbo;
    private int mVertexCount;

    private float mFuseTimer = FUSE_DURATION;
    private float mFlashTimer;
    private bool mFlashOn;

    public TntEntity(Vector3 position, Texture worldAtlas)
    {
        Position = position + new Vector3(0, 0.01f, 0);
        Width = 0.98f;
        Height = 0.98f;
        mWorldAtlas = worldAtlas;
        BuildCubeMesh();
        Game.Instance?.AudioManager.PlayAudio("Resources/Audio/TNTHiss.ogg", Game.Instance.AudioManager.SfxVol);
    }

    private void BuildCubeMesh()
    {
        var top = UvHelper.FromTileCoords(6, 5);
        var side = UvHelper.FromTileCoords(5, 5);

        float u0t = top.TopLeft.X, v0t = top.TopLeft.Y, u1t = top.BottomRight.X, v1t = top.BottomRight.Y;
        float u0s = side.TopLeft.X, v0s = side.TopLeft.Y, u1s = side.BottomRight.X, v1s = side.BottomRight.Y;

        var verts = new List<float>();

        // Vertex order and UVs ported from ChunkMeshBuilder so winding matches the terrain renderer.

        // Front (+Z)
        V(verts, 0, 0, 1, u0s, v0s, 0, 0, 1); V(verts, 1, 1, 1, u1s, v1s, 0, 0, 1); V(verts, 0, 1, 1, u0s, v1s, 0, 0, 1);
        V(verts, 0, 0, 1, u0s, v0s, 0, 0, 1); V(verts, 1, 0, 1, u1s, v0s, 0, 0, 1); V(verts, 1, 1, 1, u1s, v1s, 0, 0, 1);

        // Back (-Z)
        V(verts, 0, 0, 0, u1s, v0s, 0, 0, -1); V(verts, 0, 1, 0, u1s, v1s, 0, 0, -1); V(verts, 1, 1, 0, u0s, v1s, 0, 0, -1);
        V(verts, 0, 0, 0, u1s, v0s, 0, 0, -1); V(verts, 1, 1, 0, u0s, v1s, 0, 0, -1); V(verts, 1, 0, 0, u0s, v0s, 0, 0, -1);

        // Top (+Y)
        V(verts, 0, 1, 0, u0t, v0t, 0, 1, 0); V(verts, 1, 1, 1, u1t, v1t, 0, 1, 0); V(verts, 1, 1, 0, u1t, v0t, 0, 1, 0);
        V(verts, 0, 1, 0, u0t, v0t, 0, 1, 0); V(verts, 0, 1, 1, u0t, v1t, 0, 1, 0); V(verts, 1, 1, 1, u1t, v1t, 0, 1, 0);

        // Bottom (-Y)
        V(verts, 0, 0, 0, u0t, v1t, 0, -1, 0); V(verts, 1, 0, 0, u1t, v1t, 0, -1, 0); V(verts, 1, 0, 1, u1t, v0t, 0, -1, 0);
        V(verts, 0, 0, 0, u0t, v1t, 0, -1, 0); V(verts, 1, 0, 1, u1t, v0t, 0, -1, 0); V(verts, 0, 0, 1, u0t, v0t, 0, -1, 0);

        // Right (+X)
        V(verts, 1, 0, 0, u1s, v0s, 1, 0, 0); V(verts, 1, 1, 0, u1s, v1s, 1, 0, 0); V(verts, 1, 1, 1, u0s, v1s, 1, 0, 0);
        V(verts, 1, 0, 0, u1s, v0s, 1, 0, 0); V(verts, 1, 1, 1, u0s, v1s, 1, 0, 0); V(verts, 1, 0, 1, u0s, v0s, 1, 0, 0);

        // Left (-X)
        V(verts, 0, 0, 1, u1s, v0s, -1, 0, 0); V(verts, 0, 1, 1, u1s, v1s, -1, 0, 0); V(verts, 0, 1, 0, u0s, v1s, -1, 0, 0);
        V(verts, 0, 0, 1, u1s, v0s, -1, 0, 0); V(verts, 0, 1, 0, u0s, v1s, -1, 0, 0); V(verts, 0, 0, 0, u0s, v0s, -1, 0, 0);

        float[] arr = verts.ToArray();
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

    private static void V(List<float> v, float px, float py, float pz, float u, float vv, float nx, float ny, float nz)
    {
        v.Add(px); v.Add(py); v.Add(pz);
        v.Add(u); v.Add(vv);
        v.Add(nx); v.Add(ny); v.Add(nz);
    }

    public override void Tick(World world)
    {
        float dt = TickSystem.TICK_DURATION;

        mFuseTimer -= dt;

        // Flash interval shrinks from 0.5s to 0.1s as the fuse runs out.
        float t = MathF.Max(0f, 1f - mFuseTimer / FUSE_DURATION);
        float flashInterval = MathHelper.Lerp(FLASH_INTERVAL_START, FLASH_INTERVAL_END, t);
        mFlashTimer -= dt;
        if (mFlashTimer <= 0f)
        {
            mFlashTimer = flashInterval;
            mFlashOn = !mFlashOn;
        }

        // Friction
        Velocity = new Vector3(
            Velocity.X * FRICTION_COEFFICIENT,
            Velocity.Y,
            Velocity.Z * FRICTION_COEFFICIENT
        );

        base.Tick(world);

        if (mFuseTimer <= 0f)
        {
            Explode(world);
            IsAlive = false;
        }
    }

    private void Explode(World world)
    {
        Game.Instance?.AudioManager.PlayAudio("Resources/Audio/TNTExpload.ogg", Game.Instance.AudioManager.SfxVol);

        int cx = (int)MathF.Floor(Position.X);
        int cy = (int)MathF.Floor(Position.Y);
        int cz = (int)MathF.Floor(Position.Z);

        // Block destruction
        for (int dx = -EXPLOSION_RADIUS; dx <= EXPLOSION_RADIUS; dx++)
        {
            for (int dy = -EXPLOSION_RADIUS; dy <= EXPLOSION_RADIUS; dy++)
            {
                for (int dz = -EXPLOSION_RADIUS; dz <= EXPLOSION_RADIUS; dz++)
                {
                    float dist = MathF.Sqrt(dx * dx + dy * dy + dz * dz);

                    if (dist > EXPLOSION_RADIUS)
                        continue;

                    int bx = cx + dx, by = cy + dy, bz = cz + dz;
                    var block = world.GetBlock(bx, by, bz);

                    if (block == BlockType.Air)
                        continue;

                    var blockDef = BlockRegistry.Get(block);
                    if (!blockDef.IsBreakable)
                        continue;

                    // Power scales linearly from full at center to zero at radius edge.
                    float effectivePower = EXPLOSION_POWER * (1f - dist / EXPLOSION_RADIUS);
                    if (effectivePower < blockDef.Hardness)
                        continue;

                    Game.Instance?.ParticleSystem?.SpawnBlockBreakParticles(new Vector3(bx, by, bz), block);
                    world.SetBlock(bx, by, bz, BlockType.Air);
                }
            }
        }

        // Entity damage and knockback
        Vector3 center = new(cx + 0.5f, cy + 0.5f, cz + 0.5f);
        ApplyExplosionToEntity(Game.Instance?.GetPlayer, center);
        foreach (var entity in world.Entities)
        {
            if (entity == this)
                continue;
            ApplyExplosionToEntity(entity, center);
        }
    }

    private void ApplyExplosionToEntity(Entity? entity, Vector3 center)
    {
        if (entity == null || !entity.IsAlive)
            return;

        Vector3 entityCenter = entity.Position + new Vector3(0, entity.Height * 0.5f, 0);
        Vector3 delta = entityCenter - center;
        float dist = delta.Length;

        if (dist > EXPLOSION_RADIUS)
            return;

        float proximity = 1f - dist / EXPLOSION_RADIUS;

        int damage = (int)(proximity * EXPLOSION_POWER * 20f);
        if (damage > 0)
            entity.TakeDamage(damage);

        Vector3 knockDir = dist > 0.01f ? delta / dist : Vector3.UnitY;
        entity.Velocity += knockDir * proximity * KNOCKBACK_STRENGTH;
    }

    protected override void DrawModel(Matrix4 view, Matrix4 projection)
    {
        _shader?.SetFloat("uHitFlash", mFlashOn ? 0.85f : 0f);

        Matrix4 mvp = Matrix4.CreateTranslation(Position) * view * projection;
        _shader?.SetMatrix4("mvp", mvp);

        mWorldAtlas.Use(TextureUnit.Texture0);
        GL.BindVertexArray(mVao);
        GL.DrawArrays(PrimitiveType.Triangles, 0, mVertexCount);
    }

    public override void Dispose()
    {
        GL.DeleteVertexArray(mVao);
        GL.DeleteBuffer(mVbo);
    }
}
