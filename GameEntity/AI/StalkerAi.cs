// Stalker AI — chases the player; suppresses movement while the fuse is burning. | DA | 3/2/26
// The fuse logic itself lives in Stalker.cs (hysteresis, timer, explosion).
// StalkerAi handles detection, pathfinding, and wandering via HostileEntityAi.

using VoxelEngine.Terrain;

namespace VoxelEngine.GameEntity.AI;

public class StalkerAi : HostileEntityAi
{
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