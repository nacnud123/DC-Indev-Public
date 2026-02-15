// Minecraft-style light propagation using BFS flood-fill. Processes both sunlight and block light emission with incremental updates. | DA | 2/5/26
using VoxelEngine.Terrain.Blocks;

namespace VoxelEngine.Terrain;

public class LightingEngine
{
    private const int MAX_UPDATES_PER_TICK = 2048;

    private readonly World mWorld;

    private readonly Queue<LightNode> mSkyLightQueue = new();
    private readonly Queue<LightRemovalNode> mSkyRemovalQueue = new();
    private readonly Queue<LightNode> mBlockLightQueue = new();
    private readonly Queue<LightRemovalNode> mBlockRemovalQueue = new();

    private struct LightNode
    {
        public int X, Y, Z;

        public LightNode(int x, int y, int z)
        {
            X = x;
            Y = y;
            Z = z;
        }
    }

    private struct LightRemovalNode
    {
        public int X, Y, Z;
        public byte OldLight;

        public LightRemovalNode(int x, int y, int z, byte oldLight)
        {
            X = x;
            Y = y;
            Z = z;
            OldLight = oldLight;
        }
    }

    public LightingEngine(World world)
    {
        mWorld = world;
    }

    public bool HasPendingUpdates =>
        mSkyLightQueue.Count > 0 || mSkyRemovalQueue.Count > 0 ||
        mBlockLightQueue.Count > 0 || mBlockRemovalQueue.Count > 0;
    
    // Runs onc when a chunk is first generated.
    // For each column, start with light = 15 at the top. Walk downwards. At each block, subtract that block's LightOpacity. Set the sky light at each position to the remaining light value. Also, scan for any light-emitting blocks and enqueue them for block light propagation.
    public void CalculateInitialLighting(Chunk chunk)
    {
        for (int x = 0; x < Chunk.WIDTH; x++)
        {
            for (int z = 0; z < Chunk.DEPTH; z++)
            {
                int light = Chunk.MAX_LIGHT;
                for (int y = Chunk.HEIGHT - 1; y >= 0; y--)
                {
                    int opacity = BlockRegistry.GetBlockOpacity(chunk.GetBlock(x, y, z));
                    light = Math.Max(0, light - opacity);

                    chunk.SetSkyLightDirect(x, y, z, (byte)light);
                }
            }
        }

        int worldOffsetX = chunk.ChunkX * Chunk.WIDTH;
        int worldOffsetZ = chunk.ChunkZ * Chunk.DEPTH;

        for (int x = 0; x < Chunk.WIDTH; x++)
        {
            for (int z = 0; z < Chunk.DEPTH; z++)
            {
                for (int y = 0; y < Chunk.HEIGHT; y++)
                {
                    int emission = BlockRegistry.Get(chunk.GetBlock(x, y, z)).LightEmission;
                    if (emission > 0)
                    {
                        int worldX = worldOffsetX + x;
                        int worldZ = worldOffsetZ + z;

                        chunk.SetBlockLightDirect(x, y, z, (byte)emission);
                        mBlockLightQueue.Enqueue(new LightNode(worldX, y, worldZ));
                    }
                }
            }
        }
    }

    public void PropagateAllSunlight()
    {
        for (int cx = 0; cx < mWorld.SizeInChunks; cx++)
        {
            for (int cz = 0; cz < mWorld.SizeInChunks; cz++)
            {
                var chunk = mWorld.GetChunk(cx, cz);

                if (chunk == null)
                    continue;

                int worldOffsetX = cx * Chunk.WIDTH;
                int worldOffsetZ = cz * Chunk.DEPTH;

                for (int x = 0; x < Chunk.WIDTH; x++)
                {
                    for (int z = 0; z < Chunk.DEPTH; z++)
                    {
                        for (int y = 0; y < Chunk.HEIGHT; y++)
                        {
                            if (chunk.GetSkyLight(x, y, z) > 1)
                                mSkyLightQueue.Enqueue(new LightNode(worldOffsetX + x, y, worldOffsetZ + z));
                        }
                    }
                }
            }
        }

        ProcessSkyLightQueue(int.MaxValue);
    }

    public void PropagateAllBlockLight()
    {
        ProcessBlockLightQueue(int.MaxValue);
    }

    // If the block blocks light, zero out sky light at that position, enqueue it for removal. Also walk downward and remove sky light from all blocks below until hitting another opaque block.
    public void OnBlockPlaced(int x, int y, int z, BlockType newBlock)
    {
        if (BlockRegistry.BlocksLight(newBlock))
        {
            byte oldSkyLight = (byte)mWorld.GetSkyLight(x, y, z);
            if (oldSkyLight > 0)
            {
                mWorld.SetSkyLightDirect(x, y, z, 0);
                mSkyRemovalQueue.Enqueue(new LightRemovalNode(x, y, z, oldSkyLight));
                mWorld.MarkChunkDirtyAt(x, z);
            }

            for (int belowY = y - 1; belowY >= 0; belowY--)
            {
                if (BlockRegistry.BlocksLight(mWorld.GetBlock(x, belowY, z)))
                    break;

                int currentSkyLight = mWorld.GetSkyLight(x, belowY, z);
                if (currentSkyLight > 0)
                {
                    mWorld.SetSkyLightDirect(x, belowY, z, 0);
                    mSkyRemovalQueue.Enqueue(new LightRemovalNode(x, belowY, z, (byte)currentSkyLight));
                }
            }

            byte oldBlockLight = (byte)mWorld.GetBlockLight(x, y, z);
            if (oldBlockLight > 0)
            {
                mWorld.SetBlockLightDirect(x, y, z, 0);
                mBlockRemovalQueue.Enqueue(new LightRemovalNode(x, y, z, oldBlockLight));
                mWorld.MarkChunkDirtyAt(x, z);
            }
        }

        int emission = BlockRegistry.Get(newBlock).LightEmission;
        if (emission > 0)
        {
            mWorld.SetBlockLightDirect(x, y, z, (byte)emission);
            mBlockLightQueue.Enqueue(new LightNode(x, y, z));
            mWorld.MarkChunkDirtyAt(x, z);
        }
    }

    // When block is removed, if it was a light emitter, zero out block light, enqueue for removal. Check if the position now has sky access. Similarly fill in block light from neighbors.
    public void OnBlockRemoved(int x, int y, int z, BlockType oldBlock)
    {
        int oldEmission = BlockRegistry.Get(oldBlock).LightEmission;
        if (oldEmission > 0)
        {
            byte currentBlockLight = (byte)mWorld.GetBlockLight(x, y, z);
            mWorld.SetBlockLightDirect(x, y, z, 0);
            mBlockRemovalQueue.Enqueue(new LightRemovalNode(x, y, z, currentBlockLight));
            mWorld.MarkChunkDirtyAt(x, z);
        }

        bool hasSkyAccess = true;
        for (int checkY = y + 1; checkY < Chunk.HEIGHT; checkY++)
        {
            if (BlockRegistry.BlocksLight(mWorld.GetBlock(x, checkY, z)))
            {
                hasSkyAccess = false;
                break;
            }
        }

        if (hasSkyAccess)
        {
            mWorld.SetSkyLightDirect(x, y, z, Chunk.MAX_LIGHT);
            mSkyLightQueue.Enqueue(new LightNode(x, y, z));
            mWorld.MarkChunkDirtyAt(x, z);

            for (int belowY = y - 1; belowY >= 0; belowY--)
            {
                if (BlockRegistry.BlocksLight(mWorld.GetBlock(x, belowY, z)))
                    break;

                mWorld.SetSkyLightDirect(x, belowY, z, Chunk.MAX_LIGHT);
                mSkyLightQueue.Enqueue(new LightNode(x, belowY, z));
            }
        }
        else if (oldEmission == 0)
        {
            int maxNeighborSkyLight = GetMaxNeighborSkyLight(x, y, z);
            if (maxNeighborSkyLight > 1)
            {
                mWorld.SetSkyLightDirect(x, y, z, (byte)(maxNeighborSkyLight - 1));
                mSkyLightQueue.Enqueue(new LightNode(x, y, z));
                mWorld.MarkChunkDirtyAt(x, z);
            }
        }

        if (oldEmission == 0)
        {
            int maxNeighborBlockLight = GetMaxNeighborBlockLight(x, y, z);
            if (maxNeighborBlockLight > 1)
            {
                mWorld.SetBlockLightDirect(x, y, z, (byte)(maxNeighborBlockLight - 1));
                mBlockLightQueue.Enqueue(new LightNode(x, y, z));
                mWorld.MarkChunkDirtyAt(x, z);
            }
        }
    }

    public void ProcessTick()
    {
        int updates = 0;

        while (mSkyRemovalQueue.Count > 0 && updates < MAX_UPDATES_PER_TICK)
        {
            updates++;
            ProcessSkyLightRemoval(mSkyRemovalQueue.Dequeue());
        }

        while (mBlockRemovalQueue.Count > 0 && updates < MAX_UPDATES_PER_TICK)
        {
            updates++;
            ProcessBlockLightRemoval(mBlockRemovalQueue.Dequeue());
        }

        int skyUpdates = ProcessSkyLightQueue((MAX_UPDATES_PER_TICK - updates) / 2);
        updates += skyUpdates;

        ProcessBlockLightQueue(MAX_UPDATES_PER_TICK - updates);
    }

    private int GetMaxNeighborSkyLight(int x, int y, int z)
    {
        int max = 0;
        max = Math.Max(max, mWorld.GetSkyLight(x + 1, y, z));
        max = Math.Max(max, mWorld.GetSkyLight(x - 1, y, z));
        max = Math.Max(max, mWorld.GetSkyLight(x, y + 1, z));
        max = Math.Max(max, mWorld.GetSkyLight(x, y - 1, z));
        max = Math.Max(max, mWorld.GetSkyLight(x, y, z + 1));
        max = Math.Max(max, mWorld.GetSkyLight(x, y, z - 1));
        return max;
    }

    private int GetMaxNeighborBlockLight(int x, int y, int z)
    {
        int max = 0;
        max = Math.Max(max, mWorld.GetBlockLight(x + 1, y, z));
        max = Math.Max(max, mWorld.GetBlockLight(x - 1, y, z));
        max = Math.Max(max, mWorld.GetBlockLight(x, y + 1, z));
        max = Math.Max(max, mWorld.GetBlockLight(x, y - 1, z));
        max = Math.Max(max, mWorld.GetBlockLight(x, y, z + 1));
        max = Math.Max(max, mWorld.GetBlockLight(x, y, z - 1));
        return max;
    }

    // Dequeue a node, read its current light level. For each of the 6 neighbors, calculate newLight = currentLight - 1 - neighborOpacity. If newLight > the neighbor's current light, update it and enqueue the neighbor.
    private int ProcessSkyLightQueue(int maxUpdates)
    {
        int updates = 0;

        while (mSkyLightQueue.Count > 0 && updates < maxUpdates)
        {
            var node = mSkyLightQueue.Dequeue();
            updates++;

            int currentLight = mWorld.GetSkyLight(node.X, node.Y, node.Z);

            if (currentLight <= 1)
                continue;

            int newLight = currentLight - 1;
            TryPropagateSky(node.X + 1, node.Y, node.Z, newLight);
            TryPropagateSky(node.X - 1, node.Y, node.Z, newLight);
            TryPropagateSky(node.X, node.Y + 1, node.Z, newLight);
            TryPropagateSky(node.X, node.Y - 1, node.Z, newLight);
            TryPropagateSky(node.X, node.Y, node.Z + 1, newLight);
            TryPropagateSky(node.X, node.Y, node.Z - 1, newLight);
        }

        return updates;
    }

    private void TryPropagateSky(int x, int y, int z, int newLight)
    {
        if (y < 0 || y >= Chunk.HEIGHT)
            return;

        int opacity = BlockRegistry.GetBlockOpacity(mWorld.GetBlock(x, y, z));
        int propagatedLight = newLight - opacity;

        if (propagatedLight > mWorld.GetSkyLight(x, y, z))
        {
            mWorld.SetSkyLightDirect(x, y, z, (byte)propagatedLight);
            mSkyLightQueue.Enqueue(new LightNode(x, y, z));
            mWorld.MarkChunkDirtyAt(x, z);
        }
    }

    private void ProcessSkyLightRemoval(LightRemovalNode node)
    {
        CheckSkyRemovalNeighbor(node.X + 1, node.Y, node.Z, node.OldLight);
        CheckSkyRemovalNeighbor(node.X - 1, node.Y, node.Z, node.OldLight);
        CheckSkyRemovalNeighbor(node.X, node.Y + 1, node.Z, node.OldLight);
        CheckSkyRemovalNeighbor(node.X, node.Y - 1, node.Z, node.OldLight);
        CheckSkyRemovalNeighbor(node.X, node.Y, node.Z + 1, node.OldLight);
        CheckSkyRemovalNeighbor(node.X, node.Y, node.Z - 1, node.OldLight);
    }

    private void CheckSkyRemovalNeighbor(int x, int y, int z, byte oldLight)
    {
        if (y < 0 || y >= Chunk.HEIGHT)
            return;

        int neighborLight = mWorld.GetSkyLight(x, y, z);

        if (neighborLight != 0 && neighborLight < oldLight)
        {
            mWorld.SetSkyLightDirect(x, y, z, 0);
            mSkyRemovalQueue.Enqueue(new LightRemovalNode(x, y, z, (byte)neighborLight));
            mWorld.MarkChunkDirtyAt(x, z);
        }
        else if (neighborLight >= oldLight)
        {
            mSkyLightQueue.Enqueue(new LightNode(x, y, z));
        }
    }

    private int ProcessBlockLightQueue(int maxUpdates)
    {
        int updates = 0;

        while (mBlockLightQueue.Count > 0 && updates < maxUpdates)
        {
            var node = mBlockLightQueue.Dequeue();
            updates++;

            int currentLight = mWorld.GetBlockLight(node.X, node.Y, node.Z);

            if (currentLight <= 1)
                continue;

            int newLight = currentLight - 1;
            TryPropagateBlock(node.X + 1, node.Y, node.Z, newLight);
            TryPropagateBlock(node.X - 1, node.Y, node.Z, newLight);
            TryPropagateBlock(node.X, node.Y + 1, node.Z, newLight);
            TryPropagateBlock(node.X, node.Y - 1, node.Z, newLight);
            TryPropagateBlock(node.X, node.Y, node.Z + 1, newLight);
            TryPropagateBlock(node.X, node.Y, node.Z - 1, newLight);
        }

        return updates;
    }

    private void TryPropagateBlock(int x, int y, int z, int newLight)
    {
        if (y < 0 || y >= Chunk.HEIGHT)
            return;

        int opacity = BlockRegistry.GetBlockOpacity(mWorld.GetBlock(x, y, z));
        int propagatedLight = newLight - opacity;

        if (propagatedLight > mWorld.GetBlockLight(x, y, z))
        {
            mWorld.SetBlockLightDirect(x, y, z, (byte)propagatedLight);
            mBlockLightQueue.Enqueue(new LightNode(x, y, z));
            mWorld.MarkChunkDirtyAt(x, z);
        }
    }

    private void ProcessBlockLightRemoval(LightRemovalNode node)
    {
        CheckBlockRemovalNeighbor(node.X + 1, node.Y, node.Z, node.OldLight);
        CheckBlockRemovalNeighbor(node.X - 1, node.Y, node.Z, node.OldLight);
        CheckBlockRemovalNeighbor(node.X, node.Y + 1, node.Z, node.OldLight);
        CheckBlockRemovalNeighbor(node.X, node.Y - 1, node.Z, node.OldLight);
        CheckBlockRemovalNeighbor(node.X, node.Y, node.Z + 1, node.OldLight);
        CheckBlockRemovalNeighbor(node.X, node.Y, node.Z - 1, node.OldLight);
    }

    private void CheckBlockRemovalNeighbor(int x, int y, int z, byte oldLight)
    {
        if (y < 0 || y >= Chunk.HEIGHT)
            return;

        int neighborLight = mWorld.GetBlockLight(x, y, z);

        if (neighborLight != 0 && neighborLight < oldLight)
        {
            mWorld.SetBlockLightDirect(x, y, z, 0);
            mBlockRemovalQueue.Enqueue(new LightRemovalNode(x, y, z, (byte)neighborLight));
            mWorld.MarkChunkDirtyAt(x, z);
        }
        else if (neighborLight >= oldLight)
        {
            mBlockLightQueue.Enqueue(new LightNode(x, y, z));
        }
    }
}
