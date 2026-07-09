// Main AStar pathfinding script - Ported over from previous C# / Unity project | DA | 2/5/26

using System;
using System.Collections.Generic;

using VoxelEngine.Terrain;

namespace VoxelEngine.GameEntity.AI.Pathfinding;

/// <summary>
/// A* Pathfinding algorithm for 3D voxel world navigation. Operates on block-grid coordinates (Vector3i) rather than continuous world space - each node is one block position, and movement is restricted to the 4 cardinal horizontal directions plus implicit single-block step-up/step-down (see FindNeighbours). Not thread-safe: an instance's working sets (open/closed sets, node cache) are cleared and reused on every FindPath call, so concurrent calls on the same instance would corrupt each other's search state. Each AI-controlled entity owns its own instance (see EntityAi).
/// </summary>
public class AStarPathfinder
{
    // Search is capped to bound worst-case per-tick cost; if unmet by then, FindPath gives up and returns null (caller falls back to wandering/no movement).
    private const int MAX_ITERATIONS = 200;

    // Reused across calls to avoid reallocating PathNode instances every search; cleared at the start of each FindPath.
    private readonly Dictionary<Vector3i, PathNode> mNodeCache = new();
    // Positions that have already been fully expanded (won't be revisited).
    private readonly HashSet<Vector3i> mClosedSet = new();
    // Min-heap keyed by F-score (G + H); determines expansion order.
    private readonly PriorityQueue<PathNode, int> mOpenQueue = new();
    // Mirrors the contents of mOpenQueue as a fast O(1) "is this position already queued" check, since PriorityQueue<T> doesn't support that lookup natively and can contain stale duplicates.
    private readonly HashSet<Vector3i> mOpenSet = new();
    // Scratch buffer for FindNeighbours' results, reused per-expansion to avoid per-call allocation.
    private readonly List<PathNode> mNeighbourBuffer = new(6);

    // The 4 cardinal horizontal directions (no diagonals) used for neighbour expansion: west, east, north, south, paired index-for-index with DZ.
    private static readonly int[] DX = { -1, 1, 0, 0 };
    private static readonly int[] DZ = { 0, 0, -1, 1 };

    /// <summary>
    /// Finds the shortest path between two positions in the world using standard A* with a Manhattan-distance heuristic. Returns a Stack of block positions ordered so the *next* step to take is on top (see BuildPath), or null if no path was found within MAX_ITERATIONS or the open set is exhausted.
    /// </summary>
    public Stack<Vector3i>? FindPath(World world, Vector3i start, Vector3i goal)
    {
        // Reset all search state - this instance is reused across calls (see class doc).
        mNodeCache.Clear();
        mClosedSet.Clear();
        mOpenQueue.Clear();
        mOpenSet.Clear();

        var startNode = GetOrCreateNode(start);
        startNode.H = CalculateHeuristic(start, goal);
        startNode.F = startNode.H; // G=0 at the start, so F = H.
        mOpenQueue.Enqueue(startNode, startNode.F);
        mOpenSet.Add(start);

        int iterations = 0;

        while (mOpenSet.Count > 0 && iterations < MAX_ITERATIONS)
        {
            iterations++;

            // Pop the lowest F-score node. Note: because of the "stale re-enqueue" pattern below, mOpenQueue can contain duplicate/stale entries for the same position; mOpenSet.Remove is what actually keeps membership consistent, and any stale dequeued duplicate simply won't match mOpenSet.Contains checks on later entries (its PathNode is a shared reference so its fields already reflect the best-known values regardless of which queue entry we dequeue).
            var current = mOpenQueue.Dequeue();
            mOpenSet.Remove(current.Position);

            if (current.Position == goal)
                return BuildPath(current, start);

            mClosedSet.Add(current.Position);

            FindNeighbours(world, current.Position, start);

            foreach (var neighbour in mNeighbourBuffer)
            {
                if (mClosedSet.Contains(neighbour.Position))
                    continue;

                int tentativeG = current.G + CalculateGScore(neighbour.Position, current.Position);

                if (mOpenSet.Contains(neighbour.Position))
                {
                    // Already queued - only update if this path to it is cheaper.
                    if (tentativeG < neighbour.G)
                    {
                        neighbour.Parent = current;
                        neighbour.G = tentativeG;
                        neighbour.F = neighbour.G + neighbour.H;
                        // Re-enqueue with updated priority (old entry becomes stale)
                        mOpenQueue.Enqueue(neighbour, neighbour.F);
                    }
                }
                else
                {
                    // First time seeing this position - compute its heuristic and queue it.
                    neighbour.Parent = current;
                    neighbour.G = tentativeG;
                    neighbour.H = CalculateHeuristic(neighbour.Position, goal);
                    neighbour.F = neighbour.G + neighbour.H;
                    mOpenQueue.Enqueue(neighbour, neighbour.F);
                    mOpenSet.Add(neighbour.Position);
                }
            }
        }

        // Exhausted the open set or hit the iteration cap without reaching the goal.
        return null;
    }

    /// <summary>
    /// Computes the next movement direction to reach the goal.
    /// </summary>
    public Vector3 ComputeDirection(World world, Vector3i start, Vector3i goal)
    {
        var path = FindPath(world, start, goal);

        if (path == null || path.Count == 0)
            return Vector3.Zero;

        Vector3i nextPos = path.Peek();
        return new Vector3(
            nextPos.X - start.X,
            nextPos.Y - start.Y,
            nextPos.Z - start.Z
        );
    }

    /// <summary>
    /// Finds valid neighbouring nodes for pathfinding: the 4 cardinal horizontal neighbours at the same Y level, or (if the same-level spot isn't walkable) a step-up or step-down variant one block above/below. This is what allows mobs to path up/down single-block stairs/ledges without needing diagonal or jump-specific move types. Writes results into mNeighbourBuffer to avoid per-call allocation - callers must consume the buffer before the next FindNeighbours call.
    /// </summary>
    private void FindNeighbours(World world, Vector3i parentPosition, Vector3i start)
    {
        mNeighbourBuffer.Clear();

        for (int i = 0; i < 4; i++)
        {
            Vector3i neighbourPos = new(
                parentPosition.X + DX[i],
                parentPosition.Y,
                parentPosition.Z + DZ[i]
            );

            // Never re-add the start node as a neighbour (would create a trivial cycle).
            if (neighbourPos == start)
                continue;

            if (IsWalkable(world, neighbourPos))
            {
                mNeighbourBuffer.Add(GetOrCreateNode(neighbourPos));
            }
            else
            {
                // Same-level spot is blocked - try treating it as a step up or down instead of a flat move. Both are tried (not mutually exclusive) since terrain could legitimately allow either from this parent, depending on the neighbour. Check if we can step up one block
                Vector3i stepUpPos = new(neighbourPos.X, neighbourPos.Y + 1, neighbourPos.Z);
                if (IsWalkable(world, stepUpPos))
                    mNeighbourBuffer.Add(GetOrCreateNode(stepUpPos));

                // Check if we can step down one block
                Vector3i stepDownPos = new(neighbourPos.X, neighbourPos.Y - 1, neighbourPos.Z);
                if (IsWalkable(world, stepDownPos))
                    mNeighbourBuffer.Add(GetOrCreateNode(stepDownPos));
            }
        }
    }

    /// <summary>
    /// Checks if a position is walkable: requires a solid block directly below (a floor to stand on - air below means it's an open drop, not a valid step) and two air blocks at the position itself (feet level) and one above (head level), i.e. enough clearance for a 2-block-tall entity to occupy that space.
    /// </summary>
    private bool IsWalkable(World world, Vector3i position)
    {
        // Need solid ground below
        BlockType groundBlock = world.GetBlock(position.X, position.Y - 1, position.Z);
        if (groundBlock == BlockType.Air)
            return false;

        // Need air at feet level
        BlockType feetBlock = world.GetBlock(position.X, position.Y, position.Z);
        if (feetBlock != BlockType.Air)
            return false;

        // Need air at head level
        BlockType headBlock = world.GetBlock(position.X, position.Y + 1, position.Z);
        if (headBlock != BlockType.Air)
            return false;

        return true;
    }

    /// <summary>
    /// Calculates the movement cost (G-score contribution) of stepping from `current` to `neighbour`. Base cost of 10 per horizontal step (matches the *10 scale used by CalculateHeuristic so G and H stay comparable); a flat +5 surcharge is added for any vertical change (step up or down) to mildly discourage unnecessary elevation changes in favour of flatter routes, without needing true diagonal-distance math.
    /// </summary>
    private int CalculateGScore(Vector3i neighbour, Vector3i current)
    {
        int gScore = 10;
        int dy = Math.Abs(current.Y - neighbour.Y);
        if (dy > 0)
            gScore += 5;
        return gScore;
    }

    /// <summary>
    /// Calculates the Manhattan (taxicab) distance heuristic for 3D grid positions, scaled by 10 to match CalculateGScore's per-step cost unit. Manhattan distance is admissible here since movement is restricted to axis-aligned single-block steps (no diagonals), so A* is guaranteed to find an optimal path.
    /// </summary>
    private int CalculateHeuristic(Vector3i from, Vector3i to)
    {
        return (Math.Abs(from.X - to.X) + Math.Abs(from.Y - to.Y) + Math.Abs(from.Z - to.Z)) * 10;
    }

    /// <summary>
    /// Walks the Parent chain from the goal node back to start, pushing each position onto a Stack. Because positions are pushed in goal-to-start order, the resulting Stack naturally has the *next* step (closest to start) on top when popped/peeked by callers like HostileEntityAi.MoveAlongPath.
    /// </summary>
    private Stack<Vector3i> BuildPath(PathNode goalNode, Vector3i start)
    {
        Stack<Vector3i> path = new();
        var current = goalNode;

        while (current.Position != start)
        {
            path.Push(current.Position);
            if (current.Parent == null)
                break; // Defensive: shouldn't normally happen before reaching start.
            current = current.Parent;
        }

        return path;
    }

    /// <summary>
    /// Gets the cached PathNode for a position, or creates and caches a new one. Reusing nodes by position (rather than allocating fresh ones per visit) is what lets the open-set / closed-set membership checks and "update G if cheaper" logic work correctly, since all references to a given position resolve to the same node instance.
    /// </summary>
    private PathNode GetOrCreateNode(Vector3i position)
    {
        if (mNodeCache.TryGetValue(position, out PathNode? node))
            return node;

        node = new PathNode(position);
        mNodeCache[position] = node;
        return node;
    }

    /// <summary>
    /// Clears the node cache. Not called automatically anywhere in the search itself (FindPath already clears it at the start of every call) - exists for callers that want to proactively release memory, e.g. when a mob despawns.
    /// </summary>
    public void ClearCache()
    {
        mNodeCache.Clear();
    }
}
