// Stalker AI - chases the player; suppresses movement while the fuse is burning. | DA | 3/2/26 The fuse logic itself lives in Stalker.cs (hysteresis, timer, explosion). StalkerAi handles detection, pathfinding, and wandering via HostileEntityAi.

using VoxelEngine.Terrain;

namespace VoxelEngine.GameEntity.AI;

/// <summary>
/// Hostile AI for the Stalker mob (a Creeper-like enemy that primes an explosive fuse when close to the player). This class only handles detection/chase/wander via the inherited HostileEntityAi state machine; it does not own the fuse timer or explosion itself - that lives on Stalker.cs, which sets <see cref="FuseActive"/> to freeze this AI's movement while the fuse is counting down (including any hysteresis logic for re-arming/canceling the fuse if the player backs away).
/// </summary>
public class StalkerAi : HostileEntityAi
{
    // Set/cleared by Stalker.cs. While true, movement/pathfinding is entirely suppressed so the mob stands still (and visibly primed) as it's about to explode.
    public bool FuseActive { get; set; }

    public StalkerAi(Entity entity) : base(entity)
    {
    }

    public override void Tick(World world)
    {
        if (FuseActive)
            return; // Stalker stands still while the fuse is burning

        base.Tick(world);
    }
}