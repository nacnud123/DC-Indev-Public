// AStar node | DA | 2/5/26
using OpenTK.Mathematics;

namespace VoxelEngine.GameEntity.AI.Pathfinding;

/// <summary>
/// Represents a node in the A* pathfinding grid.
/// </summary>
public class PathNode
{
    /// <summary> F = G + H (total estimated cost) </summary>
    public int F { get; set; }

    /// <summary> G = cost from start to this node </summary>
    public int G { get; set; }

    /// <summary> H = heuristic estimate from this node to goal </summary>
    public int H { get; set; }

    /// <summary> Parent node in the path </summary>
    public PathNode? Parent { get; set; }

    /// <summary> Position in world coordinates (block position) </summary>
    public Vector3i Position { get; set; }

    public PathNode(Vector3i position)
    {
        Position = position;
    }

    public void Reset()
    {
        F = 0;
        G = 0;
        H = 0;
        Parent = null;
    }
}
