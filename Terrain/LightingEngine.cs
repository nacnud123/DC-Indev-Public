// Minecraft-style light propagation using BFS flood-fill. Processes both sunlight and block light emission with incremental updates. | DA | 2/5/26
using VoxelEngine.Terrain.Blocks;

namespace VoxelEngine.Terrain;

public class LightingEngine
{
    private const int MAX_UPDATES_PER_TICK = 2048;

    private readonly World mWorld;
    private readonly Queue<LightNode> mLightQueue = new();
    private readonly Queue<LightRemovalNode> mRemovalQueue = new();

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

    public bool HasPendingUpdates => mLightQueue.Count > 0 || mRemovalQueue.Count > 0;

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

                    chunk.SetLightDirect(x, y, z, (byte)light);
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

                        if (emission > mWorld.GetLight(worldX, y, worldZ))
                        {
                            mWorld.SetLightDirect(worldX, y, worldZ, (byte)emission);
                            mLightQueue.Enqueue(new LightNode(worldX, y, worldZ));
                        }
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
                            if (chunk.GetLight(x, y, z) > 1)
                                mLightQueue.Enqueue(new LightNode(worldOffsetX + x, y, worldOffsetZ + z));
                        }
                    }
                }
            }
        }

        ProcessLightQueue(int.MaxValue);
    }

    public void OnBlockPlaced(int x, int y, int z, BlockType newBlock)
    {
        if (BlockRegistry.BlocksLight(newBlock))
        {
            byte oldLight = (byte)mWorld.GetLight(x, y, z);
            
            if (oldLight > 0)
            {
                mWorld.SetLightDirect(x, y, z, 0);
                mRemovalQueue.Enqueue(new LightRemovalNode(x, y, z, oldLight));
                mWorld.MarkChunkDirtyAt(x, z);
            }

            for (int belowY = y - 1; belowY >= 0; belowY--)
            {
                if (BlockRegistry.BlocksLight(mWorld.GetBlock(x, belowY, z)))
                    break;

                int currentLight = mWorld.GetLight(x, belowY, z);
                if (currentLight > 0)
                {
                    mWorld.SetLightDirect(x, belowY, z, 0);
                    mRemovalQueue.Enqueue(new LightRemovalNode(x, belowY, z, (byte)currentLight));
                }
            }
        }

        int emission = BlockRegistry.Get(newBlock).LightEmission;
        
        if (emission > 0)
        {
            mWorld.SetLightDirect(x, y, z, (byte)emission);
            mLightQueue.Enqueue(new LightNode(x, y, z));
            mWorld.MarkChunkDirtyAt(x, z);
        }
    }

    public void OnBlockRemoved(int x, int y, int z, BlockType oldBlock)
    {
        int oldEmission = BlockRegistry.Get(oldBlock).LightEmission;
        
        if (oldEmission > 0)
        {
            byte currentLight = (byte)mWorld.GetLight(x, y, z);
            mWorld.SetLightDirect(x, y, z, 0);
            mRemovalQueue.Enqueue(new LightRemovalNode(x, y, z, currentLight));
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
            mWorld.SetLightDirect(x, y, z, Chunk.MAX_LIGHT);
            mLightQueue.Enqueue(new LightNode(x, y, z));
            mWorld.MarkChunkDirtyAt(x, z);

            for (int belowY = y - 1; belowY >= 0; belowY--)
            {
                if (BlockRegistry.BlocksLight(mWorld.GetBlock(x, belowY, z)))
                    break;
                
                mWorld.SetLightDirect(x, belowY, z, Chunk.MAX_LIGHT);
                mLightQueue.Enqueue(new LightNode(x, belowY, z));
            }
        }
        else if (oldEmission == 0)
        {
            int maxNeighborLight = GetMaxNeighborLight(x, y, z);
            
            if (maxNeighborLight > 1)
            {
                mWorld.SetLightDirect(x, y, z, (byte)(maxNeighborLight - 1));
                mLightQueue.Enqueue(new LightNode(x, y, z));
                mWorld.MarkChunkDirtyAt(x, z);
            }
        }
    }

    public void ProcessTick()
    {
        int updates = 0;

        while (mRemovalQueue.Count > 0 && updates < MAX_UPDATES_PER_TICK)
        {
            updates++;
            ProcessLightRemoval(mRemovalQueue.Dequeue());
        }

        ProcessLightQueue(MAX_UPDATES_PER_TICK - updates);
    }

    private int GetMaxNeighborLight(int x, int y, int z)
    {
        int max = 0;
        max = Math.Max(max, mWorld.GetLight(x + 1, y, z));
        max = Math.Max(max, mWorld.GetLight(x - 1, y, z));
        max = Math.Max(max, mWorld.GetLight(x, y + 1, z));
        max = Math.Max(max, mWorld.GetLight(x, y - 1, z));
        max = Math.Max(max, mWorld.GetLight(x, y, z + 1));
        max = Math.Max(max, mWorld.GetLight(x, y, z - 1));
        return max;
    }

    private int ProcessLightQueue(int maxUpdates)
    {
        int updates = 0;

        while (mLightQueue.Count > 0 && updates < maxUpdates)
        {
            var node = mLightQueue.Dequeue();
            updates++;

            int currentLight = mWorld.GetLight(node.X, node.Y, node.Z);
            
            if (currentLight <= 1) 
                continue;

            int newLight = currentLight - 1;
            TryPropagate(node.X + 1, node.Y, node.Z, newLight);
            TryPropagate(node.X - 1, node.Y, node.Z, newLight);
            TryPropagate(node.X, node.Y + 1, node.Z, newLight);
            TryPropagate(node.X, node.Y - 1, node.Z, newLight);
            TryPropagate(node.X, node.Y, node.Z + 1, newLight);
            TryPropagate(node.X, node.Y, node.Z - 1, newLight);
        }

        return updates;
    }

    private void TryPropagate(int x, int y, int z, int newLight)
    {
        if (y < 0 || y >= Chunk.HEIGHT)
            return;

        int opacity = BlockRegistry.GetBlockOpacity(mWorld.GetBlock(x, y, z));
        int propagatedLight = newLight - opacity;

        if (propagatedLight > mWorld.GetLight(x, y, z))
        {
            mWorld.SetLightDirect(x, y, z, (byte)propagatedLight);
            mLightQueue.Enqueue(new LightNode(x, y, z));
            mWorld.MarkChunkDirtyAt(x, z);
        }
    }

    private void ProcessLightRemoval(LightRemovalNode node)
    {
        CheckRemovalNeighbor(node.X + 1, node.Y, node.Z, node.OldLight);
        CheckRemovalNeighbor(node.X - 1, node.Y, node.Z, node.OldLight);
        CheckRemovalNeighbor(node.X, node.Y + 1, node.Z, node.OldLight);
        CheckRemovalNeighbor(node.X, node.Y - 1, node.Z, node.OldLight);
        CheckRemovalNeighbor(node.X, node.Y, node.Z + 1, node.OldLight);
        CheckRemovalNeighbor(node.X, node.Y, node.Z - 1, node.OldLight);
    }

    private void CheckRemovalNeighbor(int x, int y, int z, byte oldLight)
    {
        if (y < 0 || y >= Chunk.HEIGHT) 
            return;

        int neighborLight = mWorld.GetLight(x, y, z);

        if (neighborLight != 0 && neighborLight < oldLight)
        {
            mWorld.SetLightDirect(x, y, z, 0);
            mRemovalQueue.Enqueue(new LightRemovalNode(x, y, z, (byte)neighborLight));
            mWorld.MarkChunkDirtyAt(x, z);
        }
        else if (neighborLight >= oldLight)
        {
            mLightQueue.Enqueue(new LightNode(x, y, z));
        }
    }
}