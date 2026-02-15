// Main AStar pathfinding script - Ported over from previous C# / Unity project | DA | 2/5/26

using System;
using System.Collections.Generic;
using OpenTK.Mathematics;
using VoxelEngine.Terrain;

namespace VoxelEngine.GameEntity.AI.Pathfinding;

/// <summary>
/// A* Pathfinding algorithm for 3D voxel world navigation.
/// </summary>
public class AStarPathfinder
{
    private const int MAX_ITERATIONS = 200;

    private readonly Dictionary<Vector3i, PathNode> mNodeCache = new();
    private readonly HashSet<Vector3i> mClosedSet = new();
    private readonly PriorityQueue<PathNode, int> mOpenQueue = new();
    private readonly HashSet<Vector3i> mOpenSet = new();
    private readonly List<PathNode> mNeighbourBuffer = new(6);

    private static readonly int[] DX = { -1, 1, 0, 0 };
    private static readonly int[] DZ = { 0, 0, -1, 1 };

    /// <summary>
    /// Finds the shortest path between two positions in the world.
    /// </summary>
    public Stack<Vector3i>? FindPath(World world, Vector3i start, Vector3i goal)
    {
        mNodeCache.Clear();
        mClosedSet.Clear();
        mOpenQueue.Clear();
        mOpenSet.Clear();

        var startNode = GetOrCreateNode(start);
        startNode.H = CalculateHeuristic(start, goal);
        startNode.F = startNode.H;
        mOpenQueue.Enqueue(startNode, startNode.F);
        mOpenSet.Add(start);

        int iterations = 0;

        while (mOpenSet.Count > 0 && iterations < MAX_ITERATIONS)
        {
            iterations++;

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
                    neighbour.Parent = current;
                    neighbour.G = tentativeG;
                    neighbour.H = CalculateHeuristic(neighbour.Position, goal);
                    neighbour.F = neighbour.G + neighbour.H;
                    mOpenQueue.Enqueue(neighbour, neighbour.F);
                    mOpenSet.Add(neighbour.Position);
                }
            }
        }

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
    /// Finds valid neighbouring nodes for pathfinding.
    /// Writes results into mNeighbourBuffer to avoid allocation.
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

            if (neighbourPos == start)
                continue;

            if (IsWalkable(world, neighbourPos))
            {
                mNeighbourBuffer.Add(GetOrCreateNode(neighbourPos));
            }
            else
            {
                // Check if we can step up one block
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
    /// Checks if a position is walkable (has ground below and space for entity).
    /// </summary>
    private static bool IsWalkable(World world, Vector3i position)
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
    /// Calculates the movement cost between two positions.
    /// </summary>
    private static int CalculateGScore(Vector3i neighbour, Vector3i current)
    {
        int gScore = 10;
        int dy = Math.Abs(current.Y - neighbour.Y);
        if (dy > 0)
            gScore += 5;
        return gScore;
    }

    /// <summary>
    /// Calculates Manhattan distance heuristic for 3D.
    /// </summary>
    private static int CalculateHeuristic(Vector3i from, Vector3i to)
    {
        return (Math.Abs(from.X - to.X) + Math.Abs(from.Y - to.Y) + Math.Abs(from.Z - to.Z)) * 10;
    }

    /// <summary>
    /// Builds the final path from goal node back to start.
    /// </summary>
    private static Stack<Vector3i> BuildPath(PathNode goalNode, Vector3i start)
    {
        Stack<Vector3i> path = new();
        var current = goalNode;

        while (current.Position != start)
        {
            path.Push(current.Position);
            if (current.Parent == null)
                break;
            current = current.Parent;
        }

        return path;
    }

    /// <summary>
    /// Gets or creates a node at the given position.
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
    /// Clears the node cache. Call periodically to free memory.
    /// </summary>
    public void ClearCache()
    {
        mNodeCache.Clear();
    }
}
