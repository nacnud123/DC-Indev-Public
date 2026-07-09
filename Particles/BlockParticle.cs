// Holds structs for particles | DA | 2/5/26


namespace VoxelEngine.Particles;

/// <summary>
/// Plain-data state for a single block-break debris particle (spawned e.g. when a block is destroyed). Rendered as a small billboard quad textured with a random sub-tile sampled from the source block's texture (see UvHelper.GetRandomSubTile), giving the illusion of chunky block fragments. Simulated each frame by ParticleSystem.Update (gravity + simple collision against the world) and removed once Lifetime reaches zero.
/// </summary>
public struct BlockParticle
{
    // World-space position of the particle.
    public Vector3 Pos;
    // World-space velocity, integrated each frame and affected by Gravity.
    public Vector3 Vel;
    // Top-left UV coordinate of the sampled sub-tile texture region (normalized atlas space).
    public Vector2 UvOffset;
    // Width/height (in normalized UV space) of the sampled sub-tile texture region.
    public Vector2 UvSize;
    // Half-size of the billboard quad in world units.
    public float Size;
    // Remaining time (seconds) before the particle expires and is removed.
    public float Lifetime;
    // Downward acceleration applied per second; reduced when the particle is in water.
    public float Gravity;
}

/// <summary>
/// Plain-data state for a single smoke puff particle (e.g. from furnaces/fire). Unlike BlockParticle it isn't textured from the block atlas - it fades out over its lifetime via alpha (see MaxLifetime/Lifetime ratio used in ParticleSystem.RenderSmoke) and drifts upward rather than falling.
/// </summary>
public struct SmokeParticle
{
    public Vector3 Pos;
    public Vector3 Vel;
    public float Size;
    // Remaining time (seconds) before the particle expires and is removed.
    public float Lifetime;
    // Original Lifetime value at spawn time; Lifetime/MaxLifetime gives the fade-out alpha fraction (1 = fully opaque/new, 0 = about to disappear).
    public float MaxLifetime;
    // Set at spawn (see ParticleSystem.SpawnSmokeParticle) but note UpdateSmoke currently applies a hardcoded upward drift (0.1f) rather than reading this field - kept here for parity with BlockParticle / potential future use.
    public float Gravity;
}
