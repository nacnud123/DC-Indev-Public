// Main chunk file. Holds stuff related to chunk, like the blocks inside of it. Has some rendering functions, has functions to get and set lighting at positions, and has functions to rebuild the chunk's mesh | DA | 2/14/26
// Added in new Metadata which allows block to remember what direction they were facing. Important for stairs and torches. | DA | 2/21/26
using System.Runtime.InteropServices;
using OpenTK.Graphics.OpenGL4;
using OpenTK.Mathematics;
using VoxelEngine.Rendering;
using VoxelEngine.Saving;
using VoxelEngine.Terrain.Blocks;

namespace VoxelEngine.Terrain;

public class Chunk
{
    public const int WIDTH = 16;
    public const int HEIGHT = 128;
    public const int DEPTH = 16;
    public const int MAX_LIGHT = 15;
    private const int VERTEX_STRIDE = 11;
    private const int VOLUME = WIDTH * HEIGHT * DEPTH;
    private const float WATER_SURFACE_HEIGHT = 14f / 16f;

    internal enum Face
    {
        Front,
        Back,
        Top,
        Bottom,
        Right,
        Left
    }

    private static readonly (Face face, int dx, int dy, int dz)[] FaceDirections =
    [
        (Face.Front, 0, 0, 1),
        (Face.Back, 0, 0, -1),
        (Face.Top, 0, 1, 0),
        (Face.Bottom, 0, -1, 0),
        (Face.Right, 1, 0, 0),
        (Face.Left, -1, 0, 0)
    ];

    public int ChunkX { get; }
    public int ChunkZ { get; }

    private readonly byte[] mBlocks;
    private readonly byte[] mSkyLightLevels;
    private readonly byte[] mBlockLightLevels;
    private readonly byte[] mMetadata;

    private readonly World mWorld;

    private int mVao, mVbo;
    private int mVertexCount;
    private int mTransVao, mTransVbo;
    private int mTransVertexCount;
    private bool mIsDirty = true;
    private bool mChunkModified = false;
    private bool mIsGpuInitialized;
    private bool mIsTransGpuInitialized;

    public bool IsLoaded { get; set; }
    public bool HasChunkBeenModified { get => mChunkModified; set => mChunkModified = value; }

    private List<float>? mVertexBuffer;
    private List<float>? mTransVertexBuffer;

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

    private static int GetIndex(int x, int y, int z) => x + z * WIDTH + y * WIDTH * DEPTH;

    public BlockType GetBlock(int x, int y, int z)
    {
        if (x < 0 || x >= WIDTH || y < 0 || y >= HEIGHT || z < 0 || z >= DEPTH)
            return BlockType.Air;

        return (BlockType)mBlocks[GetIndex(x, y, z)];
    }

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

    public int GetMetadata(int x, int y, int z)
    {
        if (x < 0 || x >= WIDTH || y < 0 || y >= HEIGHT || z < 0 || z >= DEPTH)
            return 0;

        int index = GetIndex(x, y, z);
        int byteIndex = index / 2;

        return (index & 1) == 0 ? mMetadata[byteIndex] & 0x0F : (mMetadata[byteIndex] >> 4) & 0x0F;
    }

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
                        // Deterministic visual offset per block position
                        int hash = (int)(wx * 3129871) ^ ((int)wz * 116129781);
                        hash = hash * hash * 42317861 + hash * 11;
                        float offsetX = ((hash >> 16 & 15) / 15f - 0.5f) * 0.5f;
                        float offsetZ = ((hash >> 24 & 15) / 15f - 0.5f) * 0.5f;

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
                        ChunkMeshBuilder.AddFire(transVertices, wx, y, wz, block, GetSkyLightAt(x, y, z), GetBlockLightAt(x, y, z));
                        continue;
                    }

                    if (block == BlockType.Water)
                        BuildWaterFaces(transVertices, x, y, z, wx, wz, block);
                    else if (block == BlockType.Lava)
                        BuildLavaFaces(transVertices, x, y, z, wx, wz, block);
                    else if (block == BlockType.Furnace || block == BlockType.Chest)
                        BuildFacingBlockFaces(vertices, x, y, z, wx, wz, block);
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

    private void BuildBlockFaces(List<float> verts, int x, int y, int z, float wx, float wz, BlockType block)
    {
        foreach (var (face, dx, dy, dz) in FaceDirections)
        {
            int nx = x + dx, ny = y + dy, nz = z + dz;
            
            if (IsTransparent(nx, ny, nz))
                ChunkMeshBuilder.AddFace(verts, wx, y, wz, face, block, GetSkyLightAt(nx, ny, nz), GetBlockLightAt(nx, ny, nz));
        }
    }

    private void BuildFacingBlockFaces(List<float> verts, int x, int y, int z, float wx, float wz, BlockType block)
    {
        int facing = GetMetadata(x, y, z);
        var topTex = BlockRegistry.GetTopTexture(block);
        var bottomTex = BlockRegistry.GetBottomTexture(block);
        var sideTex = BlockRegistry.GetSideTexture(block);
        var frontTex = BlockRegistry.GetFrontTexture(block);

        foreach (var (face, dx, dy, dz) in FaceDirections)
        {
            int nx = x + dx, ny = y + dy, nz = z + dz;
            if (!IsTransparent(nx, ny, nz))
                continue;

            var tex = face switch
            {
                Face.Top => topTex,
                Face.Bottom => bottomTex,
                _ => GetFacingTexture(face, facing, frontTex, sideTex)
            };

            ChunkMeshBuilder.AddFace(verts, wx, y, wz, face, tex, GetSkyLightAt(nx, ny, nz), GetBlockLightAt(nx, ny, nz));
        }
    }

    // Metadata encoding: 0=North(-Z), 1=South(+Z), 2=East(+X), 3=West(-X).
    // The block faces the player, so its front is opposite to the camera's look direction.
    private static TextureCoords GetFacingTexture(Face geometricFace, int facing, TextureCoords frontTex, TextureCoords sideTex)
    {
        Face frontFace = facing switch
        {
            0 => Face.Front,
            1 => Face.Back,
            2 => Face.Left,
            3 => Face.Right,
            _ => Face.Front
        };

        return geometricFace == frontFace ? frontTex : sideTex;
    }

    private void BuildSlabFaces(List<float> verts, int x, int y, int z, float wx, float wz, BlockType block)
    {
        float slabTop = y + 0.5f;

        // Top face is always rendered
        ChunkMeshBuilder.AddFace(verts, wx, y, wz, Face.Top, block, GetSkyLightAt(x, y + 1, z), GetBlockLightAt(x, y + 1, z), slabTop);

        // Bottom + 4 sides: normal transparency culling
        foreach (var (face, dx, dy, dz) in FaceDirections)
        {
            if (face == Face.Top)
                continue;

            int nx = x + dx, ny = y + dy, nz = z + dz;
            
            if (IsTransparent(nx, ny, nz))
                ChunkMeshBuilder.AddFace(verts, wx, y, wz, face, block, GetSkyLightAt(nx, ny, nz), GetBlockLightAt(nx, ny, nz), slabTop);
        }
    }

    private void BuildStairFaces(List<float> verts, int x, int y, int z, float wx, float wz, BlockType block, int facing)
    {
        int skyLight = GetSkyLightAt(x, y, z);
        int blockLight = GetBlockLightAt(x, y, z);

        // Box 1: bottom slab (full X/Z, bottom half)
        ChunkMeshBuilder.AddStairBox(verts, wx, y, wz, wx + 1, y + 0.5f, wz + 1, block, skyLight, blockLight);

        // Box 2: back step (half extent on one axis based on facing, top half)
        // Facing: 0=North(-Z), 1=South(+Z), 2=East(+X), 3=West(-X)
        // The back step is on the opposite side from where the player faces
        switch (facing)
        {
            case 0: // North: step back is -Z half
                ChunkMeshBuilder.AddStairBox(verts, wx, y + 0.5f, wz, wx + 1, y + 1, wz + 0.5f, block, skyLight, blockLight);
                break;
            case 1: // South: step back is +Z half
                ChunkMeshBuilder.AddStairBox(verts, wx, y + 0.5f, wz + 0.5f, wx + 1, y + 1, wz + 1, block, skyLight, blockLight);
                break;
            case 2: // East: step back is +X half
                ChunkMeshBuilder.AddStairBox(verts, wx + 0.5f, y + 0.5f, wz, wx + 1, y + 1, wz + 1, block, skyLight, blockLight);
                break;
            case 3: // West: step back is -X half
                ChunkMeshBuilder.AddStairBox(verts, wx, y + 0.5f, wz, wx + 0.5f, y + 1, wz + 1, block, skyLight, blockLight);
                break;
        }
    }

    private void BuildWaterFaces(List<float> verts, int x, int y, int z, float wx, float wz, BlockType block)
    {
        bool waterAbove = GetBlockAt(x, y + 1, z) == BlockType.Water;
        float waterTop = waterAbove ? y + 1f : y + WATER_SURFACE_HEIGHT;

        foreach (var (face, dx, dy, dz) in FaceDirections)
        {
            if (face == Face.Top)
            {
                if (!waterAbove)
                    ChunkMeshBuilder.AddFace(verts, wx, y, wz, face, block, GetSkyLightAt(x, y + 1, z), GetBlockLightAt(x, y + 1, z), waterTop);
            }
            else if (ShouldDrawWaterFace(x + dx, y + dy, z + dz))
            {
                ChunkMeshBuilder.AddFace(verts, wx, y, wz, face, block, GetSkyLightAt(x + dx, y + dy, z + dz), GetBlockLightAt(x + dx, y + dy, z + dz), waterTop);
            }
        }
    }

    private void BuildLavaFaces(List<float> verts, int x, int y, int z, float wx, float wz, BlockType block)
    {
        bool lavaAbove = GetBlockAt(x, y + 1, z) == BlockType.Lava;
        float lavaTop = lavaAbove ? y + 1f : y + WATER_SURFACE_HEIGHT;

        foreach (var (face, dx, dy, dz) in FaceDirections)
        {
            if (face == Face.Top)
            {
                if (!lavaAbove)
                    ChunkMeshBuilder.AddFace(verts, wx, y, wz, face, block, GetSkyLightAt(x, y + 1, z), GetBlockLightAt(x, y + 1, z), lavaTop);
            }
            else if (ShouldDrawLavaFace(x + dx, y + dy, z + dz))
            {
                ChunkMeshBuilder.AddFace(verts, wx, y, wz, face, block, GetSkyLightAt(x + dx, y + dy, z + dz), GetBlockLightAt(x + dx, y + dy, z + dz), lavaTop);
            }
        }
    }

    private bool ShouldDrawLavaFace(int x, int y, int z)
    {
        var neighbor = GetBlockAt(x, y, z);
        return neighbor != BlockType.Lava && BlockRegistry.IsTransparent(neighbor);
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

    private bool ShouldDrawWaterFace(int x, int y, int z)
    {
        var neighbor = GetBlockAt(x, y, z);
        return neighbor != BlockType.Water && BlockRegistry.IsTransparent(neighbor);
    }

    private bool IsTransparent(int x, int y, int z)
    {
        if (y < 0 || y >= HEIGHT)
            return true;

        return BlockRegistry.IsTransparent(GetBlockAt(x, y, z));
    }

    private void UploadToGpu(List<float> vertices, ref int vao, ref int vbo, ref bool initialized)
    {
        if (!initialized)
        {
            vao = GL.GenVertexArray();
            vbo = GL.GenBuffer();
            initialized = true;
        }

        GL.BindVertexArray(vao);
        GL.BindBuffer(BufferTarget.ArrayBuffer, vbo);
        // Use CollectionsMarshal.AsSpan to avoid allocating a copy via ToArray()
        var span = CollectionsMarshal.AsSpan(vertices);
        GL.BufferData(BufferTarget.ArrayBuffer, span.Length * sizeof(float),
            ref MemoryMarshal.GetReference(span), BufferUsageHint.DynamicDraw);

        int stride = VERTEX_STRIDE * sizeof(float);
        GL.VertexAttribPointer(0, 3, VertexAttribPointerType.Float, false, stride, 0);
        GL.EnableVertexAttribArray(0);
        GL.VertexAttribPointer(1, 3, VertexAttribPointerType.Float, false, stride, 3 * sizeof(float));
        GL.EnableVertexAttribArray(1);
        GL.VertexAttribPointer(2, 3, VertexAttribPointerType.Float, false, stride, 6 * sizeof(float));
        GL.EnableVertexAttribArray(2);
        GL.VertexAttribPointer(3, 2, VertexAttribPointerType.Float, false, stride, 9 * sizeof(float));
        GL.EnableVertexAttribArray(3);

        GL.BindVertexArray(0);
    }

    public void Render()
    {
        if (mVertexCount == 0)
            return;

        GL.BindVertexArray(mVao);
        GL.DrawArrays(PrimitiveType.Triangles, 0, mVertexCount);
    }

    public void RenderTransparent()
    {
        if (mTransVertexCount == 0)
            return;

        GL.BindVertexArray(mTransVao);
        GL.DrawArrays(PrimitiveType.Triangles, 0, mTransVertexCount);
    }

    public void Dispose()
    {

        if (mIsGpuInitialized)
        {
            GL.DeleteVertexArray(mVao);
            GL.DeleteBuffer(mVbo);
        }
        if (mIsTransGpuInitialized)
        {
            GL.DeleteVertexArray(mTransVao);
            GL.DeleteBuffer(mTransVbo);
        }
    }
}
