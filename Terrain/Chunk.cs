// Main chunk file. Holds stuff related to chunk, like the blocks inside of it. Has some rendering functions, has functions to get and set lighting at positions, and has functions to rebuild the chunk's mesh | DA | 2/14/26 Added in new Metadata which allows block to remember what direction they were facing. Important for stairs and torches. | DA | 2/21/26
using System.Runtime.InteropServices;
using Silk.NET.OpenGL;

using VoxelEngine.Rendering;
using VoxelEngine.Saving;
using VoxelEngine.Terrain.Blocks;

namespace VoxelEngine.Terrain;

// A 16x128x16 piece of the world. Blocks are stored as a flat byte array (mBlocks) instead of a 3D array for speed/memory reasons - GetIndex() below converts (x,y,z) into the matching flat index. Light and metadata only need 4 bits (0-15) per block, so two of them are packed into each byte (a "nibble" each) to use half the memory - see GetSkyLight/SetSkyLightDirect for how that packing/unpacking works.
public partial class Chunk
{
    // Chunk dimensions. WIDTH/DEPTH are the horizontal footprint (16x16, matching the classic Minecraft chunk size and chosen as a power of two so world<->chunk coordinate conversion can use fast shift/mask instead of division - see World.GetBlock). HEIGHT is the full world vertical extent - there's only one "chunk" per column, it just happens to be 128 tall.
    public const int WIDTH = 16;
    public const int HEIGHT = 128;
    public const int DEPTH = 16;
    // Light levels range 0 (dark) to 15 (full brightness) since they're stored as 4-bit nibbles.
    public const int MAX_LIGHT = 15;
    // Number of floats per emitted mesh vertex: 3 position + 3 normal + 3 (light/color?) + 2 UV = 11.
    private const int VERTEX_STRIDE = 11;
    // Total block count in the chunk; used to size the flat per-block arrays.
    private const int VOLUME = WIDTH * HEIGHT * DEPTH;
    // Water (and lava) don't fill the full block height when nothing is stacked on top - the visible top surface sits at 14/16 of a block, giving a slight "meniscus" look rather than a perfectly flat 1-block-tall slab.
    private const float WATER_SURFACE_HEIGHT = 14f / 16f;

    // The 6 axis-aligned directions a cube face can point. Used both for mesh building (which faces to emit) and for texture-orientation logic (e.g. facing blocks like furnaces/chests).
    internal enum Face
    {
        Front,
        Back,
        Top,
        Bottom,
        Right,
        Left
    }

    // Pairs each Face with its (dx,dy,dz) offset to the neighboring block in that direction - walked over by every face-building method to test each of the 6 neighbors for transparency.
    private static readonly (Face face, int dx, int dy, int dz)[] FaceDirections =
    [
        (Face.Front, 0, 0, 1),
        (Face.Back, 0, 0, -1),
        (Face.Top, 0, 1, 0),
        (Face.Bottom, 0, -1, 0),
        (Face.Right, 1, 0, 0),
        (Face.Left, -1, 0, 0)
    ];

    // Position of this chunk in the World's chunk grid (not world block coordinates - multiply by WIDTH/DEPTH to get the chunk's origin in world space, as done throughout Chunk.MeshBuilding.cs).
    public int ChunkX { get; }
    public int ChunkZ { get; }

    // Flat VOLUME-length array of BlockType bytes, one full byte per block (see GetIndex for the (x,y,z) -> flat index mapping).
    private readonly byte[] mBlocks;
    // Sky/block light and metadata are each 4 bits (0-15) per block, so they're packed two per byte (VOLUME/2 bytes) - see GetSkyLight/SetSkyLightDirect for the nibble packing scheme.
    private readonly byte[] mSkyLightLevels;
    private readonly byte[] mBlockLightLevels;
    private readonly byte[] mMetadata;

    private readonly World mWorld;

    // GL handles for the opaque mesh (VAO/VBO) and the separate transparent mesh (water/glass), rendered in a second pass so transparency blends correctly against already-drawn opaque geometry.
    private uint mVao, mVbo;
    private int mVertexCount;
    private uint mTransVao, mTransVbo;
    private int mTransVertexCount;
    // Set whenever a block/metadata change means the mesh no longer matches the block data; cleared by RebuildMeshIfDirty once the mesh has been regenerated.
    private bool mIsDirty = true;
    private bool mChunkModified = false;
    // Tracks whether GenVertexArray/GenBuffer have been called yet, so UploadToGpu only allocates the GL objects once and reuses them on subsequent rebuilds.
    private bool mIsGpuInitialized;
    private bool mIsTransGpuInitialized;

    // Whether this chunk is currently within render distance (set by World.RenderChunks each frame). Chunks that aren't loaded are skipped for both rendering and scheduled ticks.
    public bool IsLoaded { get; set; }
    // Whether this chunk has changed since it was loaded/generated and therefore needs to be written back to disk on save (see World.SetChunkAsModified / Saving/Serialization).
    public bool HasChunkBeenModified { get => mChunkModified; set => mChunkModified = value; }

    // Reused scratch buffers for mesh building, cached across rebuilds to reduce GC pressure; set back to null after upload since the vertex data lives on the GPU once uploaded (see the end of RebuildMeshIfDirty).
    private List<float>? mVertexBuffer;
    private List<float>? mTransVertexBuffer;

    /// <summary>
    /// Allocates a new, empty (all-Air) chunk at the given chunk-grid coordinates. Block/light/ metadata arrays start zeroed; the chunk is not marked loaded until the caller either loads save data into it or runs terrain generation over it (see World's constructor).
    /// </summary>
    public Chunk(int chunkX, int chunkZ, World world)
    {
        ChunkX = chunkX;
        ChunkZ = chunkZ;
        mWorld = world;
        mBlocks = new byte[VOLUME];
        mSkyLightLevels = new byte[VOLUME / 2];
        mBlockLightLevels = new byte[VOLUME / 2];
        mMetadata = new byte[VOLUME / 2];

        IsLoaded = false;
    }

    // Turns a 3D block position inside this chunk into a single index into the flat mBlocks array. Layout is X-fastest, then Z, then Y (i.e. one XZ "layer" of WIDTH*DEPTH blocks per Y level): index = x + z*WIDTH + y*WIDTH*DEPTH. This means all of a single Y-layer is contiguous in memory, which matches how mesh building iterates (x outer, y middle, z inner - see RebuildMeshIfDirty) reasonably well, though it's really just a fixed convention used consistently by every accessor in this file (GetBlock, GetSkyLight, GetMetadata, etc.).
    private static int GetIndex(int x, int y, int z) => x + z * WIDTH + y * WIDTH * DEPTH;

    // Reads the block at LOCAL (chunk-relative, 0..WIDTH-1 / 0..HEIGHT-1 / 0..DEPTH-1) coordinates. Out-of-range coordinates (e.g. a mesh-building neighbor check that crosses into a different chunk) return Air rather than throwing - callers that need cross-chunk lookups use GetBlockAt in Chunk.MeshBuilding.cs, which forwards to World for out-of-range positions.
    public BlockType GetBlock(int x, int y, int z)
    {
        if (x < 0 || x >= WIDTH || y < 0 || y >= HEIGHT || z < 0 || z >= DEPTH)
            return BlockType.Air;

        return (BlockType)mBlocks[GetIndex(x, y, z)];
    }

    // Light values only need 4 bits (0-15), so two blocks' light values share one byte: even indices use the low nibble (& 0x0F), odd indices use the high nibble (>> 4).
    public int GetSkyLight(int x, int y, int z)
    {
        if (x < 0 || x >= WIDTH || y < 0 || y >= HEIGHT || z < 0 || z >= DEPTH)
            return MAX_LIGHT;

        int index = GetIndex(x, y, z);
        int byteIndex = index / 2;
        return (index & 1) == 0 ? mSkyLightLevels[byteIndex] & 0x0F : (mSkyLightLevels[byteIndex] >> 4) & 0x0F;
    }

    public void SetSkyLightDirect(int x, int y, int z, byte level)
    {
        if (x < 0 || x >= WIDTH || y < 0 || y >= HEIGHT || z < 0 || z >= DEPTH)
            return;

        int index = GetIndex(x, y, z);
        int byteIndex = index / 2;

        // Overwrite only our own nibble, keep the other block's nibble in this byte untouched.
        if ((index & 1) == 0)
            mSkyLightLevels[byteIndex] = (byte)((mSkyLightLevels[byteIndex] & 0xF0) | (level & 0x0F));
        else
            mSkyLightLevels[byteIndex] = (byte)((mSkyLightLevels[byteIndex] & 0x0F) | ((level & 0x0F) << 4));
    }

    public int GetBlockLight(int x, int y, int z)
    {
        if (x < 0 || x >= WIDTH || y < 0 || y >= HEIGHT || z < 0 || z >= DEPTH)
            return 0;

        int index = GetIndex(x, y, z);
        int byteIndex = index / 2;

        return (index & 1) == 0 ? mBlockLightLevels[byteIndex] & 0x0F : (mBlockLightLevels[byteIndex] >> 4) & 0x0F;
    }

    public void SetBlockLightDirect(int x, int y, int z, byte level)
    {
        if (x < 0 || x >= WIDTH || y < 0 || y >= HEIGHT || z < 0 || z >= DEPTH)
            return;

        int index = GetIndex(x, y, z);
        int byteIndex = index / 2;

        if ((index & 1) == 0)
            mBlockLightLevels[byteIndex] = (byte)((mBlockLightLevels[byteIndex] & 0xF0) | (level & 0x0F));
        else
            mBlockLightLevels[byteIndex] = (byte)((mBlockLightLevels[byteIndex] & 0x0F) | ((level & 0x0F) << 4));
    }

    // Reads the 4-bit metadata nibble for a block - encodes things like facing direction for stairs/torches/furnaces (see BlockTorch and Chunk.MeshBuilding.cs's GetFacingTexture for the specific meaning of each value; by convention 0 = default facing). Packed two per byte the same way as sky/block light (see GetSkyLight above for the nibble layout explanation).
    public int GetMetadata(int x, int y, int z)
    {
        if (x < 0 || x >= WIDTH || y < 0 || y >= HEIGHT || z < 0 || z >= DEPTH)
            return 0;

        int index = GetIndex(x, y, z);
        int byteIndex = index / 2;

        return (index & 1) == 0 ? mMetadata[byteIndex] & 0x0F : (mMetadata[byteIndex] >> 4) & 0x0F;
    }

    // Writes the metadata nibble (masked to 4 bits) and marks the chunk dirty so the mesh is rebuilt with the new orientation/appearance.
    public void SetMetadata(int x, int y, int z, byte value)
    {
        if (x < 0 || x >= WIDTH || y < 0 || y >= HEIGHT || z < 0 || z >= DEPTH)
            return;

        int index = GetIndex(x, y, z);
        int byteIndex = index / 2;

        if ((index & 1) == 0)
            mMetadata[byteIndex] = (byte)((mMetadata[byteIndex] & 0xF0) | (value & 0x0F));
        else
            mMetadata[byteIndex] = (byte)((mMetadata[byteIndex] & 0x0F) | ((value & 0x0F) << 4));
        MarkDirty();
    }

    // Changes the block type at a local position. Also clears that block's metadata nibble back to 0 (default facing) since metadata from the old block type is meaningless for a different block type - e.g. a stair's facing shouldn't carry over if it's replaced by dirt. Note: unlike World.SetBlock, this has no lighting/hook side effects - see World.SetBlock and World.SetBlockDirect for the two different levels of "set a block" in this codebase.
    public void SetBlock(int x, int y, int z, BlockType type)
    {
        if (x < 0 || x >= WIDTH || y < 0 || y >= HEIGHT || z < 0 || z >= DEPTH)
            return;

        int index = GetIndex(x, y, z);
        mBlocks[index] = (byte)type;

        // Clear metadata when block changes
        int byteIndex = index / 2;
        if ((index & 1) == 0)
            mMetadata[byteIndex] = (byte)(mMetadata[byteIndex] & 0xF0);
        else
            mMetadata[byteIndex] = (byte)(mMetadata[byteIndex] & 0x0F);

        MarkDirty();
    }

    // Flags this chunk so its mesh gets rebuilt soon (World.Update only rebuilds a few dirty chunks per frame - see MAX_CHUNK_REBUILDS_PER_FRAME in World.cs).
    public void MarkDirty()
    {
        if (!mIsDirty)
        {
            mIsDirty = true;
            mWorld.NotifyDirty(this);
        }
    }

    // If this chunk has been modified, rebuild its mesh
    public void RebuildMeshIfDirty()
    {
        if (!mIsDirty)
            return;

        mIsDirty = false;

        var vertices = mVertexBuffer ?? new List<float>();
        var transVertices = mTransVertexBuffer ?? new List<float>();
        vertices.Clear();
        transVertices.Clear();

        for (int x = 0; x < WIDTH; x++)
        {
            for (int y = 0; y < HEIGHT; y++)
            {
                for (int z = 0; z < DEPTH; z++)
                {
                    BlockType block = (BlockType)mBlocks[GetIndex(x, y, z)];
                    if (block == BlockType.Air)
                        continue;

                    float wx = ChunkX * WIDTH + x;
                    float wz = ChunkZ * DEPTH + z;

                    var renderType = BlockRegistry.GetRenderType(block);
                    if (renderType == RenderingType.Cross)
                    {
                        float offsetX = 0f, offsetZ = 0f;
                        if (BlockRegistry.Get(block).CrossHasOffset)
                        {
                            // Deterministic visual offset per block position
                            int hash = (int)(wx * 3129871) ^ ((int)wz * 116129781);
                            hash = hash * hash * 42317861 + hash * 11;
                            offsetX = ((hash >> 16 & 15) / 15f - 0.5f) * 0.5f;
                            offsetZ = ((hash >> 24 & 15) / 15f - 0.5f) * 0.5f;
                        }

                        ChunkMeshBuilder.AddCross(vertices, wx + offsetX, y, wz + offsetZ, block, GetSkyLightAt(x, y, z), GetBlockLightAt(x, y, z));
                        continue;
                    }

                    if (renderType == RenderingType.Slab)
                    {
                        BuildSlabFaces(vertices, x, y, z, wx, wz, block);
                        continue;
                    }

                    if (renderType == RenderingType.Torch)
                    {
                        int meta = GetMetadata(x, y, z);
                        int facing = meta == 0 ? -1 : meta - 1;
                        ChunkMeshBuilder.AddTorch(vertices, wx, y, wz, block, GetSkyLightAt(x, y, z), GetBlockLightAt(x, y, z), facing);
                        continue;
                    }

                    if (renderType == RenderingType.Stair)
                    {
                        int stairFacing = GetMetadata(x, y, z);
                        BuildStairFaces(vertices, x, y, z, wx, wz, block, stairFacing);
                        continue;
                    }

                    if (renderType == RenderingType.Fire)
                    {
                        var below = GetBlockAt(x, y - 1, z);
                        bool hasSolidBase = BlockRegistry.IsSolid(below) || BlockFire.GetEncouragement(below) > 0;
                        bool fNegX = BlockFire.GetEncouragement(GetBlockAt(x - 1, y, z)) > 0;
                        bool fPosX = BlockFire.GetEncouragement(GetBlockAt(x + 1, y, z)) > 0;
                        bool fNegZ = BlockFire.GetEncouragement(GetBlockAt(x, y, z - 1)) > 0;
                        bool fPosZ = BlockFire.GetEncouragement(GetBlockAt(x, y, z + 1)) > 0;
                        bool fAbove = BlockFire.GetEncouragement(GetBlockAt(x, y + 1, z)) > 0;
                        ChunkMeshBuilder.AddFire(transVertices, wx, y, wz, block,
                            GetSkyLightAt(x, y, z), GetBlockLightAt(x, y, z),
                            hasSolidBase, fNegX, fPosX, fNegZ, fPosZ, fAbove);
                        continue;
                    }

                    if (block == BlockType.Water)
                        BuildWaterFaces(transVertices, x, y, z, wx, wz, block);
                    else if (block == BlockType.Lava)
                        BuildLavaFaces(transVertices, x, y, z, wx, wz, block);
                    else if (block == BlockType.Furnace || block == BlockType.FurnaceLit || block == BlockType.Chest)
                        BuildFacingBlockFaces(vertices, x, y, z, wx, wz, block);
                    else if (block == BlockType.DoubleChest)
                        BuildDoubleChestFaces(vertices, x, y, z, wx, wz);
                    else if (block == BlockType.Farmland)
                        BuildFarmlandFaces(vertices, x, y, z, wx, wz);
                    else
                        BuildBlockFaces(vertices, x, y, z, wx, wz, block);
                }
            }
        }

        mVertexCount = vertices.Count / VERTEX_STRIDE;
        UploadToGpu(vertices, ref mVao, ref mVbo, ref mIsGpuInitialized);

        mTransVertexCount = transVertices.Count / VERTEX_STRIDE;
        UploadToGpu(transVertices, ref mTransVao, ref mTransVbo, ref mIsTransGpuInitialized);

        // Data is on GPU now, release managed copies
        mVertexBuffer = null;
        mTransVertexBuffer = null;
    }

    private int GetSkyLightAt(int x, int y, int z)
    {
        if (y >= HEIGHT)
            return MAX_LIGHT;

        if (y < 0)
            return 0;

        if (x >= 0 && x < WIDTH && z >= 0 && z < DEPTH)
            return GetSkyLight(x, y, z);

        int worldX = ChunkX * WIDTH + x;
        int worldZ = ChunkZ * DEPTH + z;

        return mWorld.GetSkyLight(worldX, y, worldZ);
    }

    private int GetBlockLightAt(int x, int y, int z)
    {
        if (y >= HEIGHT || y < 0)
            return 0;

        if (x >= 0 && x < WIDTH && z >= 0 && z < DEPTH)
            return GetBlockLight(x, y, z);

        int worldX = ChunkX * WIDTH + x;
        int worldZ = ChunkZ * DEPTH + z;

        return mWorld.GetBlockLight(worldX, y, worldZ);
    }

    private BlockType GetBlockAt(int x, int y, int z)
    {
        if (y < 0 || y >= HEIGHT)
            return BlockType.Air;

        if (x >= 0 && x < WIDTH && z >= 0 && z < DEPTH)
            return (BlockType)mBlocks[GetIndex(x, y, z)];

        int worldX = ChunkX * WIDTH + x;
        int worldZ = ChunkZ * DEPTH + z;
        return mWorld.GetBlock(worldX, y, worldZ);
    }

    private bool IsTransparent(int x, int y, int z)
    {
        if (y < 0 || y >= HEIGHT)
            return true;

        return BlockRegistry.IsTransparent(GetBlockAt(x, y, z));
    }

    private void UploadToGpu(List<float> vertices, ref uint vao, ref uint vbo, ref bool initialized)
    {
        var gl = VoxelEngine.Rendering.GlContext.Gl;
        if (!initialized)
        {
            vao = gl.GenVertexArray();
            vbo = gl.GenBuffer();
            initialized = true;
        }

        gl.BindVertexArray(vao);
        gl.BindBuffer(BufferTargetARB.ArrayBuffer, vbo);
        gl.BufferData<float>(BufferTargetARB.ArrayBuffer, CollectionsMarshal.AsSpan(vertices), BufferUsageARB.DynamicDraw);

        uint stride = (uint)(VERTEX_STRIDE * sizeof(float));
        gl.VertexAttribPointer(0, 3, GLEnum.Float, false, stride, (nint)0);
        gl.EnableVertexAttribArray(0);
        gl.VertexAttribPointer(1, 3, GLEnum.Float, false, stride, (nint)(3 * sizeof(float)));
        gl.EnableVertexAttribArray(1);
        gl.VertexAttribPointer(2, 3, GLEnum.Float, false, stride, (nint)(6 * sizeof(float)));
        gl.EnableVertexAttribArray(2);
        gl.VertexAttribPointer(3, 2, GLEnum.Float, false, stride, (nint)(9 * sizeof(float)));
        gl.EnableVertexAttribArray(3);

        gl.BindVertexArray(0);
    }

    public void Render()
    {
        if (mVertexCount == 0)
            return;

        var gl = VoxelEngine.Rendering.GlContext.Gl;
        gl.BindVertexArray(mVao);
        gl.DrawArrays(PrimitiveType.Triangles, 0, (uint)mVertexCount);
    }

    public void RenderTransparent()
    {
        if (mTransVertexCount == 0)
            return;

        var gl = VoxelEngine.Rendering.GlContext.Gl;
        gl.BindVertexArray(mTransVao);
        gl.DrawArrays(PrimitiveType.Triangles, 0, (uint)mTransVertexCount);
    }

    public void Dispose()
    {
        var gl = VoxelEngine.Rendering.GlContext.Gl;
        if (mIsGpuInitialized)
        {
            gl.DeleteVertexArray(mVao);
            gl.DeleteBuffer(mVbo);
        }
        if (mIsTransGpuInitialized)
        {
            gl.DeleteVertexArray(mTransVao);
            gl.DeleteBuffer(mTransVbo);
        }
    }
}
