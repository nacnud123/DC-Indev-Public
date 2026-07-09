// An interface for block entities. | DA | 3/5/26

using VoxelEngine.Terrain;

namespace VoxelEngine.BlockEntities;

/// <summary>
/// Common contract for all block entities (<see cref="ChestData"/>, <see cref="DoubleChestData"/>, <see cref="FurnaceData"/>) so <see cref="BlockEntityManager"/> can store them polymorphically in a single position-keyed dictionary and handle generic operations like destruction uniformly.
/// </summary>
public interface IBlockEntity
{
    /// <summary>World-space block position this entity is attached to; matches the key used in <see cref="BlockEntityManager"/>'s dictionary.</summary>
    public Vector3i Position { get; set; }

    /// <summary>Spawns dropped-item entities for this block entity's inventory contents, e.g. when the underlying block is broken.</summary>
    public void DropContents(World world);
}
