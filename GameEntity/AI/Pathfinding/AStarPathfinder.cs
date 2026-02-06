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
    private readonly Dictionary<Vector3i, PathNode> mNodeCache = new();
    private readonly HashSet<PathNode> mOpenList = new();
    private readonly HashSet<PathNode> mClosedList = new();

    private PathNode? mCurrentNode;

    /// <summary>
    /// Finds the shortest path between two positions in the world.
    /// </summary>
    /// <param name="world">The world to pathfind in</param>
    /// <param name="start">Start position (block coordinates)</param>
    /// <param name="goal">Goal position (block coordinates)</param>
    /// <returns>Stack of positions representing the path, or null if no path found</returns>
    public Stack<Vector3i>? FindPath(World world, Vector3i start, Vector3i goal)
    {
        mCurrentNode = GetOrCreateNode(start);
        mOpenList.Clear();
        mClosedList.Clear();
        mOpenList.Add(mCurrentNode);

        Stack<Vector3i>? path = null;

        while (mOpenList.Count > 0 && path == null)
        {
            List<PathNode> neighbours = FindNeighbours(world, mCurrentNode.Position, start);
            ExamineNeighbours(neighbours, mCurrentNode, goal);
            UpdateCurrentNode();
            path = GeneratePath(mCurrentNode, start, goal);
        }

        return path;
    }

    /// <summary>
    /// Computes the next movement direction to reach the goal.
    /// </summary>
    /// <param name="world">The world to pathfind in</param>
    /// <param name="start">Start position (block coordinates)</param>
    /// <param name="goal">Goal position (block coordinates)</param>
    /// <returns>Direction vector to move, or zero if no path</returns>
    public Vector3 ComputeDirection(World world, Vector3i start, Vector3i goal)
    {
        var path = FindPath(world, start, goal);

        if (path == null || path.Count == 0)
        {
            return Vector3.Zero;
        }

        Vector3i nextPos = path.Peek();
        return new Vector3(
            nextPos.X - start.X,
            nextPos.Y - start.Y,
            nextPos.Z - start.Z
        );
    }

    /// <summary>
    /// Finds valid neighbouring nodes for pathfinding.
    /// Only considers horizontal movement (X and Z) for ground-based entities.
    /// </summary>
    private List<PathNode> FindNeighbours(World world, Vector3i parentPosition, Vector3i start)
    {
        List<PathNode> neighbours = new();

        // Check 8 horizontal directions (no diagonal for now, just cardinal)
        int[] dx = { -1, 1, 0, 0 };
        int[] dz = { 0, 0, -1, 1 };

        for (int i = 0; i < 4; i++)
        {
            Vector3i neighbourPos = new(
                parentPosition.X + dx[i],
                parentPosition.Y,
                parentPosition.Z + dz[i]
            );

            if (neighbourPos == start)
                continue;

            if (IsWalkable(world, neighbourPos))
            {
                PathNode neighbour = GetOrCreateNode(neighbourPos);
                neighbours.Add(neighbour);
            }
            else
            {
                // Check if we can step up one block
                Vector3i stepUpPos = new(neighbourPos.X, neighbourPos.Y + 1, neighbourPos.Z);
                if (IsWalkable(world, stepUpPos))
                {
                    PathNode neighbour = GetOrCreateNode(stepUpPos);
                    neighbours.Add(neighbour);
                }

                // Check if we can step down one block
                Vector3i stepDownPos = new(neighbourPos.X, neighbourPos.Y - 1, neighbourPos.Z);
                if (IsWalkable(world, stepDownPos))
                {
                    PathNode neighbour = GetOrCreateNode(stepDownPos);
                    neighbours.Add(neighbour);
                }
            }
        }

        return neighbours;
    }

    /// <summary>
    /// Checks if a position is walkable (has ground below and space for entity).
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
    /// Examines neighbours and updates their costs.
    /// </summary>
    private void ExamineNeighbours(List<PathNode> neighbours, PathNode current, Vector3i goal)
    {
        foreach (var neighbour in neighbours)
        {
            int gScore = CalculateGScore(neighbour.Position, current.Position);

            if (mOpenList.Contains(neighbour))
            {
                if (current.G + gScore < neighbour.G)
                {
                    CalculateNodeValues(current, neighbour, gScore, goal);
                }
            }
            else if (!mClosedList.Contains(neighbour))
            {
                CalculateNodeValues(current, neighbour, gScore, goal);
                mOpenList.Add(neighbour);
            }
        }
    }

    /// <summary>
    /// Calculates F, G, H values for a node.
    /// </summary>
    private void CalculateNodeValues(PathNode parent, PathNode neighbour, int cost, Vector3i goal)
    {
        neighbour.Parent = parent;
        neighbour.G = parent.G + cost;
        neighbour.H = CalculateHeuristic(neighbour.Position, goal);
        neighbour.F = neighbour.G + neighbour.H;
    }

    /// <summary>
    /// Calculates the movement cost between two positions.
    /// </summary>
    private int CalculateGScore(Vector3i neighbour, Vector3i current)
    {
        int dx = Math.Abs(current.X - neighbour.X);
        int dy = Math.Abs(current.Y - neighbour.Y);
        int dz = Math.Abs(current.Z - neighbour.Z);

        // Base cost for movement
        int gScore = 10;

        // Extra cost for vertical movement (climbing/falling)
        if (dy > 0)
            gScore += 5;

        return gScore;
    }

    /// <summary>
    /// Calculates Manhattan distance heuristic for 3D.
    /// </summary>
    private int CalculateHeuristic(Vector3i from, Vector3i to)
    {
        return (Math.Abs(from.X - to.X) + Math.Abs(from.Y - to.Y) + Math.Abs(from.Z - to.Z)) * 10;
    }

    /// <summary>
    /// Updates the current node to the one with lowest F score.
    /// </summary>
    private void UpdateCurrentNode()
    {
        if (mCurrentNode != null)
        {
            mOpenList.Remove(mCurrentNode);
            mClosedList.Add(mCurrentNode);
        }

        if (mOpenList.Count > 0)
        {
            PathNode? minNode = null;
            int minF = int.MaxValue;

            foreach (PathNode node in mOpenList)
            {
                if (node.F < minF)
                {
                    minF = node.F;
                    minNode = node;
                }
            }

            mCurrentNode = minNode;
        }
    }

    /// <summary>
    /// Gets or creates a node at the given position.
    /// </summary>
    private PathNode GetOrCreateNode(Vector3i position)
    {
        if (mNodeCache.TryGetValue(position, out PathNode? node))
        {
            node.Reset();
            return node;
        }

        node = new PathNode(position);
        mNodeCache[position] = node;
        return node;
    }

    /// <summary>
    /// Generates the final path from current node back to start.
    /// </summary>
    private Stack<Vector3i>? GeneratePath(PathNode? current, Vector3i start, Vector3i goal)
    {
        if (current == null)
            return null;

        if (current.Position == goal)
        {
            Stack<Vector3i> finalPath = new();

            while (current.Position != start)
            {
                finalPath.Push(current.Position);

                if (current.Parent == null)
                    break;

                current = current.Parent;
            }

            return finalPath;
        }

        return null;
    }

    /// <summary>
    /// Clears the node cache. Call periodically to free memory.
    /// </summary>
    public void ClearCache()
    {
        mNodeCache.Clear();
    }
}
