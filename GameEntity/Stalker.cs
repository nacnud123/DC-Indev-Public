// Hostile entity that stalks the player and explodes when close. Flashes white during fuse. | DA | 3/2/26

using OpenTK.Mathematics;
using VoxelEngine.Core;
using VoxelEngine.GameEntity.AI;
using VoxelEngine.Items;
using VoxelEngine.Terrain;
using VoxelEngine.Terrain.Blocks;

namespace VoxelEngine.GameEntity;

public class Stalker : Entity
{
    private const string BODY_MODEL = "Resources/Entities/Stalker/StalkerBody/StalkerBody.obj";
    private const string BODY_TEXTURE = "Resources/Entities/Stalker/StalkerBody/StalkerBody.png";
    private const string HEAD_MODEL = "Resources/Entities/Stalker/StalkerHead/StalkerHead.obj";
    private const string HEAD_TEXTURE = "Resources/Entities/Stalker/StalkerHead/Head.png";
    private const string LEG_MODEL = "Resources/Entities/Stalker/StalkerLeg/StalkerLeg.obj";
    private const string LEG_TEXTURE = "Resources/Entities/Stalker/StalkerLeg/StalkerLeg.png";

    private static readonly Vector3 BodyOffset = new(.03f, 0.0625f, 0.03125f);
    private static readonly Vector3 HeadOffset = new(0f, 0.25f, 0.03125f);
    private static readonly Vector3 FrontRightLegOffset = new(0f, 0f, -0.0625f);
    private static readonly Vector3 FrontLeftLegOffset = new(-0.0625f, 0f, -0.0625f);
    private static readonly Vector3 BackRightLegOffset = new(0f, 0f, 0.0625f);
    private static readonly Vector3 BackLeftLegOffset = new(-0.0625f, 0f, 0.0625f);

    private static readonly Vector3 HeadPivot = new(0f, 0.0625f, 0f);
    private static readonly Vector3 LegPivot = new(0f, 0.0625f, 0f);

    // Movement
    private const float MAX_LEG_SWING = MathF.PI / 4f;
    private const float WALK_ANIM_SPEED = 6f;
    private const float SWING_DECAY = 0.75f;

    // Fuse
    private const float FUSE_RANGE = 3f;
    private const float FUSE_DURATION = 1.5f; // seconds until boom once in range
    private const float ABORT_RANGE = 5f; // player must back away this far to abort fuse

    // Flash
    private const float FLASH_INTERVAL_START = 0.4f;
    private const float FLASH_INTERVAL_END = 0.07f;

    // Explosion
    private const int EXPLOSION_RADIUS = 3;
    private const float EXPLOSION_POWER = 3f;
    private const float KNOCKBACK_STRENGTH = 25f;

    private readonly EntityModel mBodyModel;
    private readonly EntityModel mHeadModel;
    private readonly EntityModel mLegModel;

    private float mWalkPhase;
    private float mLegSwing;

    private bool mFuseActive;
    private float mFuseTimer;
    private float mFlashTimer;
    private bool mFlashOn;

    public override float Width
    {
        get => 0.6f;
        set { }
    }

    public override float Height
    {
        get => 1.7f;
        set { }
    }

    public override float Scale
    {
        get => 4f;
        set { }
    }

    public override float WalkSpeed
    {
        get => 2.5f;
        set { }
    }

    public override int Health { get; set; } = 20;

    private StalkerAi mStalkerAi = null!;

    public Stalker(Vector3 position)
    {
        Position = position;
        InitShader();
        mBodyModel = EntityModel.Load(BODY_MODEL, BODY_TEXTURE);
        mHeadModel = EntityModel.Load(HEAD_MODEL, HEAD_TEXTURE);
        mLegModel = EntityModel.Load(LEG_MODEL, LEG_TEXTURE);
        mStalkerAi = new StalkerAi(this);
        CurrentAI = mStalkerAi;
    }

    public override void Tick(World world)
    {
        base.Tick(world);

        float dt = TickSystem.TICK_DURATION;
        Vector3 playerPos = Game.Instance.GetPlayer.Position;
        float distToPlayer = (playerPos - Position).Length;

        UpdateFuse(world, distToPlayer, dt);

        if (!IsAlive)
            return;

        // StalkerAi.FuseActive pauses pathfinding movement while fuse burns
        mStalkerAi.FuseActive = mFuseActive;
        CurrentAI!.Tick(world);

        UpdateAnimation();
    }

    private void UpdateFuse(World world, float distToPlayer, float dt)
    {
        if (!mFuseActive)
        {
            if (distToPlayer <= FUSE_RANGE)
            {
                mFuseActive = true;
                mFuseTimer = FUSE_DURATION;
                mFlashTimer = FLASH_INTERVAL_START;
                int vol = Proximity(distToPlayer, 20f, Game.Instance.AudioManager.SfxVol);
                Game.Instance.AudioManager.PlayAudio("Resources/Audio/TNTHiss.ogg", vol);
            }

            return;
        }

        // Player backed away
        if (distToPlayer > ABORT_RANGE)
        {
            mFuseActive = false;
            mFlashOn = false;
            return;
        }

        mFuseTimer -= dt;

        float t = MathF.Max(0f, 1f - mFuseTimer / FUSE_DURATION);
        float flashInterval = MathHelper.Lerp(FLASH_INTERVAL_START, FLASH_INTERVAL_END, t);
        mFlashTimer -= dt;
        if (mFlashTimer <= 0f)
        {
            mFlashTimer = flashInterval;
            mFlashOn = !mFlashOn;
        }

        // Stand still while fuse is burning
        Velocity = new Vector3(0f, Velocity.Y, 0f);

        if (!(mFuseTimer <= 0f))
            return;

        Explode(world);
        IsAlive = false;
    }

    private void UpdateAnimation()
    {
        float dt = TickSystem.TICK_DURATION;
        float hSpeed = MathF.Sqrt(Velocity.X * Velocity.X + Velocity.Z * Velocity.Z);

        if (hSpeed > 0.01f)
        {
            mWalkPhase += hSpeed * WALK_ANIM_SPEED * dt;
            mLegSwing = 1f;
        }
        else
        {
            mLegSwing *= SWING_DECAY;

            if (mLegSwing < 0.01f)
                mLegSwing = 0f;
        }
    }

    private void Explode(World world)
    {
        Game.Instance.AudioManager.PlayAudio("Resources/Audio/TNTExpload.ogg", Game.Instance.AudioManager.SfxVol);

        int cx = (int)MathF.Floor(Position.X);
        int cy = (int)MathF.Floor(Position.Y);
        int cz = (int)MathF.Floor(Position.Z);

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

                    float power = EXPLOSION_POWER * (1f - dist / EXPLOSION_RADIUS);

                    if (power < blockDef.Hardness)
                        continue;

                    Game.Instance.ParticleSystem?.SpawnBlockBreakParticles(new Vector3(bx, by, bz), block);
                    world.SetBlock(bx, by, bz, BlockType.Air);
                }
            }
        }

        Vector3 center = new(cx + 0.5f, cy + 0.5f, cz + 0.5f);
        ApplyExplosionToEntity(Game.Instance.GetPlayer, center);

        for (int i = 0; i < world.Entities.Count; i++)
        {
            var entity = world.Entities[i];
            if (entity == this)
                continue;

            ApplyExplosionToEntity(entity, center);
        }
    }

    private void ApplyExplosionToEntity(Entity? entity, Vector3 center)
    {
        if (entity is not { IsAlive: true })
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
        // Override hit flash with fuse flash when fuse is active
        if (mFuseActive)
            _shader?.SetFloat("uHitFlash", mFlashOn ? 0.85f : 0f);

        Matrix4 entityBase = Matrix4.CreateScale(Scale) * Matrix4.CreateRotationY(Yaw) *
                             Matrix4.CreateTranslation(Position);
        Matrix4 vp = view * projection;

        DrawPart(mBodyModel, Matrix4.CreateTranslation(BodyOffset) * entityBase * vp);
        DrawPart(mHeadModel, Matrix4.CreateTranslation(HeadOffset) * entityBase * vp);

        float swing1 = MathF.Sin(mWalkPhase) * MAX_LEG_SWING * mLegSwing;
        float swing2 = MathF.Sin(mWalkPhase + MathF.PI) * MAX_LEG_SWING * mLegSwing;
        DrawLeg(swing1, FrontLeftLegOffset, entityBase, vp);
        DrawLeg(swing1, BackRightLegOffset, entityBase, vp);
        DrawLeg(swing2, FrontRightLegOffset, entityBase, vp);
        DrawLeg(swing2, BackLeftLegOffset, entityBase, vp);
    }

    private void DrawLeg(float swingAngle, Vector3 offset, Matrix4 entityBase, Matrix4 vp)
    {
        Matrix4 legLocal = Matrix4.CreateTranslation(-LegPivot)
                           * Matrix4.CreateRotationZ(swingAngle)
                           * Matrix4.CreateTranslation(LegPivot + offset);
        DrawPart(mLegModel, legLocal * entityBase * vp);
    }

    public override void TakeDamage(int amount)
    {
        base.TakeDamage(amount);
        var audio = Game.Instance.AudioManager;
        float dist = (Game.Instance.GetPlayer.Position - Position).Length;
        int vol = Proximity(dist, 20f, audio.SfxVol);

        if (vol <= 0)
            return;

        if (!IsAlive)
        {
            audio.PlayBlockBreakSound(BlockBreakMaterial.Sand);

            int count = Game.Instance.GameRandom.Next(0, 1);
            if (count > 0)
            {
                var drop = new DroppedItemEntity(Position, ItemStack.FromItem(ItemType.Sulfur, count),
                    Game.Instance.WorldTexture);

                Game.Instance.GetWorld.AddEntity(drop);
            }
        }
        else
            audio.PlayBlockContactSound(BlockBreakMaterial.Sand, vol);
    }

    protected override void Fall(World world, float dist)
    {
        int damage = (int)MathF.Ceiling(dist - 3f);
        if (damage > 0) TakeDamage(damage);
    }

    public override void Dispose()
    {
    }
}