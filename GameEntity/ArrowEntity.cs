// Projectile fired by the bow. Travels with gravity, embeds in blocks, damages entities, and can be picked up. | DA | 2/28/26

using Silk.NET.OpenGL;

using VoxelEngine.Core;
using VoxelEngine.Items;
using VoxelEngine.Rendering;
using VoxelEngine.Terrain;

namespace VoxelEngine.GameEntity;

/// <summary>
/// Projectile fired by a bow. Flies with a simple velocity-integration + drag + gravity model (note: this uses its own hand-rolled per-tick integration rather than <see cref="Physics"/>, since arrows need raycast-based hit detection against entities/blocks rather than AABB sweep collision), embeds in the first block or entity it hits, then can be picked back up by the player after a short delay. All positional constants here (SPEED, GRAVITY, DRAG_*) are tuned per-tick values, not per-second - they're applied once per game tick in <see cref="TickFlying"/>.
/// </summary>
public class ArrowEntity : Entity
{
    public override bool IsTargetable => false; // arrows can't be shot/targeted themselves
    public override float ShadowSize => 0f; // no ground shadow for a thin arrow

    public const float SPEED = 0.6f; // blocks/tick launch speed
    private const float SPREAD = 0.0075f; // random inaccuracy applied to the fire direction
    private const float DRAG_AIR = 0.99f; // per-tick velocity multiplier while flying through air
    private const float DRAG_WATER = 0.80f; // stronger per-tick drag while flying through water
    private const float GRAVITY = 0.02f; // blocks/tick^2 downward acceleration while flying
    private const int ARROW_DAMAGE = 4;
    private const int DESPAWN_TICKS = 1200; // ticks stuck in a block before the arrow disappears (~60s at 20 ticks/s)
    private const int SHAKE_TICKS = 7; // ticks the "just stuck" wobble animation lasts, and also blocks pickup during that time
    private const int OWNER_GRACE = 5; // ticks after firing before the arrow can hit its own shooter (prevents self-hit at point-blank range)
    private const float PICKUP_RADIUS = 1.5f;

    private const string TEXTURE_PATH = "Resources/arrows.png";

    // Shared mesh - built once, reused by all arrows.
    private static uint sVao;
    private static uint sVbo;
    private static int sVertexCount;
    private static bool sMeshReady;
    private static Texture? sTexture;

    private readonly Entity mOwner; // who fired this arrow (for damage attribution and the owner-hit grace period)
    private bool mInGround; // true once the arrow has embedded in a block and stopped moving
    private Vector3i mStuckBlock; // block-grid position the arrow is stuck in
    private BlockType mStuckBlockType; // block type at mStuckBlock when it stuck, used to detect if that block was later mined/changed
    private int mTicksInGround; // ticks since embedding, counts toward DESPAWN_TICKS
    private int mTicksInAir; // ticks since being fired, used for the OWNER_GRACE window
    private int mArrowShake; // countdown for the stuck-in-wood wobble animation; also blocks pickup while > 0
    private Vector3 mFacing; // normalized flight direction, used to orient the rendered arrow model

    // Player-fired constructor: derives origin and direction from the shooter's camera (for the player) or body yaw (for non-player owners), and nudges the spawn point forward so the arrow doesn't start inside the shooter's own hitbox.
    public ArrowEntity(Entity owner)
    {
        mOwner = owner;
        Width = 0.5f;
        Height = 0.5f;

        EnsureMesh();

        var player = owner as Player;

        float yawDeg = player != null ? player.Camera.Yaw : float.RadiansToDegrees(owner.Yaw);
        float pitchDeg = player != null ? player.Camera.Pitch : 0f;
        float yawRad = float.DegreesToRadians(yawDeg);
        float pitchRad = float.DegreesToRadians(pitchDeg);

        Position = player != null ? player.Camera.Position : owner.Position;

        float dirX = MathF.Cos(pitchRad) * MathF.Cos(yawRad);
        float dirY = MathF.Sin(pitchRad);
        float dirZ = MathF.Cos(pitchRad) * MathF.Sin(yawRad);

        Position += new Vector3(dirX * 0.3f, dirY * 0.3f, dirZ * 0.3f);

        SetHeading(dirX, dirY, dirZ);
    }

    // Non-player owners: spawn at origin, fire in the given normalized direction.
    public ArrowEntity(Entity owner, Vector3 origin, Vector3 direction, float spread = SPREAD)
    {
        mOwner = owner;
        Width = 0.5f;
        Height = 0.5f;

        EnsureMesh();

        Position = origin + direction * 0.3f;
        SetHeading(direction.X, direction.Y, direction.Z, spread);
    }

    // Builds the shared arrow mesh (shaft cross-quads + fletching) once on first use; all arrow instances share this single VAO/VBO and texture rather than each allocating their own.
    private static void EnsureMesh()
    {
        if (sMeshReady)
            return;

        sTexture = Texture.LoadFromFile(TEXTURE_PATH);

        // Texture atlas UVs for the shaft strip (SU/SV) and the fletching/cross-section (CU/CV), measured in a 32x32 tile region of arrows.png.
        const float SU0 = 0f, SU1 = 16f / 32f;
        const float SV0 = 1f, SV1 = 1f - 5f / 32f;
        var v = new List<float>();

        // Small offset so the two crossed shaft quads don't Z-fight (share exactly the same plane).
        const float O = 0.01f;

        // The arrow shaft is built from two quads crossed at right angles (like a classic billboard "X" cross), each double-sided (front+back pair) so it reads correctly from any viewing angle without needing to disable backface culling for the whole mesh.
        Quad(v, -8, -2, -O, SU0, SV0, 8, -2, -O, SU1, SV0, 8, 2, -O, SU1, SV1, -8, 2, -O, SU0, SV1, 0, 0, 1);
        Quad(v, -8, 2, O, SU0, SV0, 8, 2, O, SU1, SV0, 8, -2, O, SU1, SV1, -8, -2, O, SU0, SV1, 0, 0, -1);

        Quad(v, -8, O, -2, SU0, SV0, 8, O, -2, SU1, SV0, 8, O, 2, SU1, SV1, -8, O, 2, SU0, SV1, 0, 1, 0);
        Quad(v, -8, -O, 2, SU0, SV0, 8, -O, 2, SU1, SV0, 8, -O, -2, SU1, SV1, -8, -O, -2, SU0, SV1, 0, -1, 0);

        // Fletching quad near the tail end of the shaft (x = -5), using a separate UV region.
        const float CU0 = 0f, CU1 = 5f / 32f;
        const float CV0 = 1f - 5f / 32f, CV1 = 1f - 10f / 32f;
        Quad(v, -5, -2, -2, CU0, CV0, -5, -2, 2, CU1, CV0, -5, 2, 2, CU1, CV1, -5, 2, -2, CU0, CV1, -1, 0, 0);

        float[] arr = v.ToArray();
        sVertexCount = arr.Length / 8;

        var gl = GlContext.Gl;
        sVao = gl.GenVertexArray();
        sVbo = gl.GenBuffer();
        gl.BindVertexArray(sVao);
        gl.BindBuffer(BufferTargetARB.ArrayBuffer, sVbo);
        gl.BufferData<float>(BufferTargetARB.ArrayBuffer, arr, BufferUsageARB.StaticDraw);

        uint stride = (uint)(8 * sizeof(float));
        gl.VertexAttribPointer(0, 3, GLEnum.Float, false, stride, 0);
        gl.EnableVertexAttribArray(0);
        gl.VertexAttribPointer(1, 2, GLEnum.Float, false, stride, (nint)(3 * sizeof(float)));
        gl.EnableVertexAttribArray(1);
        gl.VertexAttribPointer(2, 3, GLEnum.Float, false, stride, (nint)(5 * sizeof(float)));
        gl.EnableVertexAttribArray(2);
        gl.BindVertexArray(0);

        sMeshReady = true;
    }

    // Emits two triangles (6 vertices) forming a quad from 4 corner points, all sharing one flat normal.
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
        v.Add(x); v.Add(y); v.Add(z); v.Add(u); v.Add(w); v.Add(nx); v.Add(ny); v.Add(nz);
    }

    public static void DisposeMesh()
    {
        if (!sMeshReady)
            return;

        var gl = GlContext.Gl;
        gl.DeleteVertexArray(sVao);
        gl.DeleteBuffer(sVbo);
        sTexture?.Dispose();
        sMeshReady = false;
    }

    // Normalizes the given direction, jitters it randomly by `spread` (simulating bow inaccuracy), then scales it by SPEED to set the initial launch velocity.
    private void SetHeading(float vx, float vy, float vz, float spread = SPREAD)
    {
        float len = MathF.Sqrt(vx * vx + vy * vy + vz * vz);
        if (len > 0f) { vx /= len; vy /= len; vz /= len; }

        var rng = Game.Instance.GameRandom;
        vx += ((float)rng.NextDouble() * 2f - 1f) * spread;
        vy += ((float)rng.NextDouble() * 2f - 1f) * spread;
        vz += ((float)rng.NextDouble() * 2f - 1f) * spread;

        Velocity = new Vector3(vx * SPEED, vy * SPEED, vz * SPEED);
        UpdateOrientation();
    }

    // Keeps mFacing (used for rendering the arrow's rotation) in sync with the current velocity direction; called after velocity changes each tick while flying.
    private void UpdateOrientation()
    {
        if (Velocity.LengthSquared() > 0f)
            mFacing = Vector3.Normalize(Velocity);
    }

    // Note: overrides the base Entity.Tick physics/gravity pipeline entirely - arrows use their own raycast-based flight simulation (TickFlying) instead of Physics.MoveWithCollision.
    public override void Tick(World world)
    {
        if (mArrowShake > 0)
            mArrowShake--;

        if (mInGround)
            TickStuck(world);
        else
            TickFlying(world);
    }

    // Handles an arrow that's already embedded in a block: as long as that block hasn't changed (mined, replaced, etc.) the arrow just sits there counting toward despawn and offering pickup. If the block it was stuck in disappeared, the arrow falls back into flight with a small random velocity "shake loose" kick.
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

    // Handles an arrow still in flight this tick: raycasts against solid blocks and against every targetable entity (plus the player, checked specially) over this tick's travel distance, picks whichever hit is closer, and either damages the entity, embeds in the block, or (if nothing was hit) advances position and applies drag/gravity for the next tick.
    private void TickFlying(World world)
    {
        mTicksInAir++;

        float speed = Velocity.Length();
        Vector3 dir = speed > 0f ? Velocity / speed : Vector3.Zero;

        var blockHit = world.Raycast(Position, dir, speed, solidOnly: true);

        Entity? hitEntity = null;
        float hitEntityDist = float.MaxValue;

        // Player is checked separately from world.Entities since the player isn't stored in that list. The owner-grace check prevents the arrow hitting whoever fired it in the first few ticks (e.g. shooting from very close range or slightly behind themselves).
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
            if (entity == this) continue;
            if (entity == mOwner && mTicksInAir < OWNER_GRACE) continue;
            if (!entity.IsTargetable || !entity.IsAlive) continue;

            if (entity.IsLookedAt(Position, dir, speed + 1f, out float dist)
                && dist < hitEntityDist && dist <= speed + 1f)
            {
                hitEntity = entity;
                hitEntityDist = dist;
            }
        }

        // An entity hit only "wins" over a block hit if it's actually closer along the ray - otherwise the arrow should stick in the block in front of the entity instead.
        bool entityCloser = hitEntity != null
                            && (blockHit.Type != RaycastHitType.Block || hitEntityDist < blockHit.Distance);

        if (entityCloser)
        {
            hitEntity!.TakeDamage(ARROW_DAMAGE);
            // Knockback direction follows the arrow's travel direction, with an upward bias (abs(dir.Y) + 0.6) so arrows always pop the target up a bit rather than just pushing flat.
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
            float dist = (Game.Instance.GetPlayer.Position - Position).Length();
            int vol = Proximity(dist, 20f, Game.Instance.AudioManager.SfxVol);
            Game.Instance.AudioManager.PlayAudio("Resources/Audio/Bow/ArrowHit.ogg", vol);
            return;
        }

        // Nothing hit this tick - advance the full velocity and integrate drag/gravity for the next tick. Drag is checked against the block at the arrow's new position so it slows faster once submerged in water.
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

    // Auto-picks-up the arrow into the player's inventory if they're close enough, the shake animation has finished, and there's inventory space. Silently does nothing otherwise (the arrow just stays on the ground/wall until conditions are met or it despawns).
    private void TryPickup()
    {
        if (mArrowShake > 0) return;

        var player = Game.Instance.GetPlayer;
        if ((player.Position - Position).LengthSquared() > PICKUP_RADIUS * PICKUP_RADIUS) return;

        var inv = Game.Instance.PlayerInventory;
        if (inv != null && inv.TryAdd(ItemStack.FromItem(ItemType.Arrow, 1)))
        {
            Game.Instance.AudioManager.PlayPickupSound();
            IsAlive = false;
        }
    }

    protected override void DrawModel(Matrix4x4 view, Matrix4x4 projection)
    {
        if (!sMeshReady || sTexture == null)
            return;

        // Build an orthonormal basis (fwd/up/right) that orients the arrow mesh to point along mFacing. If facing is nearly straight up/down, Vector3.UnitY would be parallel to fwd and Cross() would degenerate, so UnitZ is used as the temporary up reference instead.
        Vector3 fwd = mFacing.LengthSquared() > 0f ? mFacing : Vector3.UnitX;
        Vector3 up = MathF.Abs(Vector3.Dot(fwd, Vector3.UnitY)) > 0.99f ? Vector3.UnitZ : Vector3.UnitY;
        Vector3 right = Vector3.Normalize(Vector3.Cross(fwd, up));
        up = Vector3.Normalize(Vector3.Cross(right, fwd));

        // Rotation matrix built directly from the basis vectors as rows (equivalent to a change-of-basis / look-rotation matrix).
        var rot = new Matrix4x4(
            fwd.X,   fwd.Y,   fwd.Z,   0,
            up.X,    up.Y,    up.Z,    0,
            right.X, right.Y, right.Z, 0,
            0,       0,       0,       1);

        // Decaying wobble angle for the "just stuck in the block" shake animation - amplitude and frequency both derived from the same countdown so it winds down naturally as it expires.
        float shakeDeg = mArrowShake > 0 ? -MathF.Sin(mArrowShake * 3f) * mArrowShake : 0f;
        const float SCALE = 0.05625f; // shrinks the mesh (built in ~16-unit "pixel" space) down to block-scale

        Matrix4x4 mvp =
            Matrix4x4.CreateScale(SCALE)
            * Matrix4x4.CreateRotationX(float.DegreesToRadians(shakeDeg))
            * rot
            * Matrix4x4.CreateTranslation(Position)
            * view
            * projection;

        _shader?.SetMatrix4("mvp", mvp);
        sTexture.Use(TextureUnit.Texture0);
        var gl = GlContext.Gl;
        // Backface culling is disabled because the shaft's crossed-quad geometry needs both faces of each quad visible from any angle (see EnsureMesh) - re-enabled immediately after.
        gl.Disable(EnableCap.CullFace);
        gl.BindVertexArray(sVao);
        gl.DrawArrays(PrimitiveType.Triangles, 0, (uint)sVertexCount);
        gl.Enable(EnableCap.CullFace);
    }

    public override void Dispose()
    {
    }
}
