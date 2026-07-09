// AStar node | DA | 2/5/26


namespace VoxelEngine.GameEntity.AI.Pathfinding;

/// <summary>
/// Represents a single node in the A* search graph, one per unique block position visited during a search. Instances are created and cached by <see cref="AStarPathfinder"/>'s GetOrCreateNode and are mutated in place as the search progresses (rather than replaced), which is why costs and Parent get overwritten when a cheaper path to the same position is found.
/// </summary>
public class PathNode
{
    /// <summary> F = G + H (total estimated cost). This is the value the open-set priority queue sorts by. </summary>
    public int F { get; set; }

    /// <summary> G = actual accumulated movement cost from the start node to this node (see AStarPathfinder.CalculateGScore). </summary>
    public int G { get; set; }

    /// <summary> H = heuristic (Manhattan-distance) estimate of remaining cost from this node to the goal; never updated once set for a given search. </summary>
    public int H { get; set; }

    /// <summary> Predecessor node on the current best-known path from start to this node; used to walk the path back to front once the goal is reached. Null for the start node. </summary>
    public PathNode? Parent { get; set; }

    /// <summary> Block-grid position this node represents (integer world coordinates, one node per block). </summary>
    public Vector3i Position { get; set; }

    public PathNode(Vector3i position)
    {
        Position = position;
    }

    // Resets search-specific state so a cached node can be reused for a new search without stale cost/parent data leaking in. Note: not currently called by AStarPathfinder (which instead clears its whole node cache per search), but kept available for alternate reuse strategies.
    public void Reset()
    {
        F = 0;
        G = 0;
        H = 0;
        Parent = null;
    }
}
