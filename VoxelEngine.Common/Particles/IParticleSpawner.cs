// Particles are a pure client effect, but the code that decides to spawn them (breaking a
// block, a leaf decaying) is shared. Only the spawn half crosses into Common. | DA

using VoxelEngine.Terrain;

namespace VoxelEngine.Particles;

/// <summary>
/// The spawn half of the particle system. Update/Render stay on the client's concrete
/// <c>ParticleSystem</c>, which owns meshes and a GL context; Common only ever asks for
/// particles to exist.
/// </summary>
public interface IParticleSpawner
{
    void SpawnBlockBreakParticles(Vector3 blockPos, BlockType type);
    void SpawnSmokeParticle(Vector3 position);
}

/// <summary>Discards spawn requests. Used by the dedicated server, which has no particles.</summary>
public sealed class NullParticleSpawner : IParticleSpawner
{
    public void SpawnBlockBreakParticles(Vector3 blockPos, BlockType type) { }
    public void SpawnSmokeParticle(Vector3 position) { }
}
