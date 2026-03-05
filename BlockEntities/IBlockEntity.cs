// An interface for block entities. | DA | 3/5/26
using OpenTK.Mathematics;
using VoxelEngine.Terrain;

namespace VoxelEngine.BlockEntities;

public interface IBlockEntity
{
    public Vector3i Position { get; set; }

    public void DropContents(World world);
}
