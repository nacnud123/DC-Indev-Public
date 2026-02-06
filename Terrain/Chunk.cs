// Main chunk class, holds reference to blocks in the chunk along with it's lighting. Also, has functions used to render the chunks | DA | 2/5/26
using OpenTK.Graphics.OpenGL4;
using OpenTK.Mathematics;
using VoxelEngine.Rendering;
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

    private const float TORCH_SIZE = 2f / 16f;
    private const float TORCH_HEIGHT = 10f / 16f;
    private const float TORCH_OFFSET = 7f / 16f;

    public int ChunkX { get; }
    public int ChunkZ { get; }
    
    private readonly byte[] mBlocks;
    private readonly byte[] mLightLevels;
    
    private readonly World mWorld;
    
    private int mVao, mVbo;
    private int mVertexCount;
    private bool mIsDirty = true;
    private bool mIsGpuInitialized;

    public Chunk(int chunkX, int chunkZ, World world)
    {
        ChunkX = chunkX;
        ChunkZ = chunkZ;
        mWorld = world;
        mBlocks = new byte[VOLUME];
        mLightLevels = new byte[VOLUME / 2];
        GenerateTerrain();
    }

    private static int GetIndex(int x, int y, int z) => x + z * WIDTH + y * WIDTH * DEPTH;

    private void GenerateTerrain()
    {
        World.Current?.TerrainGen.GenerateChunkBlocks(this, DateTime.Now.Second);
    }

    public BlockType GetBlock(int x, int y, int z)
    {
        if (x < 0 || x >= WIDTH || y < 0 || y >= HEIGHT || z < 0 || z >= DEPTH)
            return BlockType.Air;

        return (BlockType)mBlocks[GetIndex(x, y, z)];
    }

    public int GetLight(int x, int y, int z)
    {
        if (x < 0 || x >= WIDTH || y < 0 || y >= HEIGHT || z < 0 || z >= DEPTH)
            return MAX_LIGHT;

        int index = GetIndex(x, y, z);
        int byteIndex = index / 2;
        // Even indices: low, odd indices: high
        return (index & 1) == 0
            ? mLightLevels[byteIndex] & 0x0F
            : (mLightLevels[byteIndex] >> 4) & 0x0F;
    }

    public void SetLightDirect(int x, int y, int z, byte level)
    {
        if (x < 0 || x >= WIDTH || y < 0 || y >= HEIGHT || z < 0 || z >= DEPTH)
            return;

        int index = GetIndex(x, y, z);
        int byteIndex = index / 2;
        // Even indices: low, odd indices: high
        if ((index & 1) == 0)
            mLightLevels[byteIndex] = (byte)((mLightLevels[byteIndex] & 0xF0) | (level & 0x0F));
        else
            mLightLevels[byteIndex] = (byte)((mLightLevels[byteIndex] & 0x0F) | ((level & 0x0F) << 4));
    }

    public void SetBlock(int x, int y, int z, BlockType type)
    {
        if (x < 0 || x >= WIDTH || y < 0 || y >= HEIGHT || z < 0 || z >= DEPTH)
            return;

        mBlocks[GetIndex(x, y, z)] = (byte)type;
        mIsDirty = true;
    }

    public void MarkDirty()
    {
        mIsDirty = true;
    }

    public void RebuildMeshIfDirty()
    {
        if (!mIsDirty)
            return;

        mIsDirty = false;

        var vertices = new List<float>();

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
                        AddCross(vertices, wx, y, wz, block, GetLightAt(x, y, z));
                        continue;
                    }
                    if (renderType == RenderingType.Torch)
                    {
                        AddTorch(vertices, wx, y, wz, block, GetLightAt(x, y, z));
                        continue;
                    }

                    if (IsTransparent(x, y, z + 1))
                        AddFace(vertices, wx, y, wz, Face.Front, block, GetLightAt(x, y, z + 1));

                    if (IsTransparent(x, y, z - 1))
                        AddFace(vertices, wx, y, wz, Face.Back, block, GetLightAt(x, y, z - 1));

                    if (IsTransparent(x, y + 1, z))
                        AddFace(vertices, wx, y, wz, Face.Top, block, GetLightAt(x, y + 1, z));

                    if (IsTransparent(x, y - 1, z))
                        AddFace(vertices, wx, y, wz, Face.Bottom, block, GetLightAt(x, y - 1, z));

                    if (IsTransparent(x + 1, y, z))
                        AddFace(vertices, wx, y, wz, Face.Right, block, GetLightAt(x + 1, y, z));

                    if (IsTransparent(x - 1, y, z))
                        AddFace(vertices, wx, y, wz, Face.Left, block, GetLightAt(x - 1, y, z));
                }
            }
        }

        mVertexCount = vertices.Count / VERTEX_STRIDE;
        UploadToGpu(vertices);
    }

    private int GetLightAt(int x, int y, int z)
    {
        if (y >= HEIGHT)
            return MAX_LIGHT;

        if (y < 0)
            return 0;

        if (x >= 0 && x < WIDTH && z >= 0 && z < DEPTH)
            return GetLight(x, y, z);

        int worldX = ChunkX * WIDTH + x;
        int worldZ = ChunkZ * DEPTH + z;

        return mWorld.GetLight(worldX, y, worldZ);
    }

    private bool IsTransparent(int x, int y, int z)
    {
        if (y < 0 || y >= HEIGHT)
            return true;

        if (x >= 0 && x < WIDTH && z >= 0 && z < DEPTH)
            return BlockRegistry.IsTransparent((BlockType)mBlocks[GetIndex(x, y, z)]);

        int worldX = ChunkX * WIDTH + x;
        int worldZ = ChunkZ * DEPTH + z;

        return BlockRegistry.IsTransparent(mWorld.GetBlock(worldX, y, worldZ));
    }

    private void UploadToGpu(List<float> vertices)
    {
        if (!mIsGpuInitialized)
        {
            mVao = GL.GenVertexArray();
            mVbo = GL.GenBuffer();
            mIsGpuInitialized = true;
        }

        GL.BindVertexArray(mVao);
        GL.BindBuffer(BufferTarget.ArrayBuffer, mVbo);
        GL.BufferData(BufferTarget.ArrayBuffer, vertices.Count * sizeof(float),
            vertices.ToArray(), BufferUsageHint.DynamicDraw);

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

    private enum Face
    {
        Front,
        Back,
        Top,
        Bottom,
        Right,
        Left
    }

    private static void AddFace(List<float> verts, float x, float y, float z, Face face, BlockType block, int lightLevel)
    {
        float faceShade = face switch
        {
            Face.Top => 1.0f,
            Face.Bottom => 0.5f,
            Face.Front or Face.Back => 0.8f,
            _ => 0.7f
        };

        float lightMultiplier = 0.1f + (lightLevel / (float)MAX_LIGHT) * 0.9f;
        float finalShade = faceShade * lightMultiplier;

        var texCoords = face switch
        {
            Face.Top => BlockRegistry.GetTopTexture(block),
            Face.Bottom => BlockRegistry.GetBottomTexture(block),
            _ => BlockRegistry.GetSideTexture(block)
        };

        float u0 = texCoords.TopLeft.X;
        float v0 = texCoords.TopLeft.Y;
        float u1 = texCoords.BottomRight.X;
        float v1 = texCoords.BottomRight.Y;

        Vector3 normal = face switch
        {
            Face.Front => Vector3.UnitZ,
            Face.Back => -Vector3.UnitZ,
            Face.Top => Vector3.UnitY,
            Face.Bottom => -Vector3.UnitY,
            Face.Right => Vector3.UnitX,
            Face.Left => -Vector3.UnitX,
            _ => Vector3.Zero
        };

        (Vector3 pos, Vector2 uv)[] faceData = face switch
        {
            Face.Front => new[]
            {
                (new Vector3(x, y, z + 1), new Vector2(u0, v0)),
                (new Vector3(x + 1, y + 1, z + 1), new Vector2(u1, v1)),
                (new Vector3(x, y + 1, z + 1), new Vector2(u0, v1)),
                (new Vector3(x, y, z + 1), new Vector2(u0, v0)),
                (new Vector3(x + 1, y, z + 1), new Vector2(u1, v0)),
                (new Vector3(x + 1, y + 1, z + 1), new Vector2(u1, v1))
            },
            Face.Back => new[]
            {
                (new Vector3(x, y, z), new Vector2(u1, v0)),
                (new Vector3(x, y + 1, z), new Vector2(u1, v1)),
                (new Vector3(x + 1, y + 1, z), new Vector2(u0, v1)),
                (new Vector3(x, y, z), new Vector2(u1, v0)),
                (new Vector3(x + 1, y + 1, z), new Vector2(u0, v1)),
                (new Vector3(x + 1, y, z), new Vector2(u0, v0))
            },
            Face.Top => new[]
            {
                (new Vector3(x, y + 1, z), new Vector2(u0, v0)),
                (new Vector3(x + 1, y + 1, z + 1), new Vector2(u1, v1)),
                (new Vector3(x + 1, y + 1, z), new Vector2(u1, v0)),
                (new Vector3(x, y + 1, z), new Vector2(u0, v0)),
                (new Vector3(x, y + 1, z + 1), new Vector2(u0, v1)),
                (new Vector3(x + 1, y + 1, z + 1), new Vector2(u1, v1))
            },
            Face.Bottom => new[]
            {
                (new Vector3(x, y, z), new Vector2(u0, v1)),
                (new Vector3(x + 1, y, z), new Vector2(u1, v1)),
                (new Vector3(x + 1, y, z + 1), new Vector2(u1, v0)),
                (new Vector3(x, y, z), new Vector2(u0, v1)),
                (new Vector3(x + 1, y, z + 1), new Vector2(u1, v0)),
                (new Vector3(x, y, z + 1), new Vector2(u0, v0))
            },
            Face.Right => new[]
            {
                (new Vector3(x + 1, y, z), new Vector2(u1, v0)),
                (new Vector3(x + 1, y + 1, z), new Vector2(u1, v1)),
                (new Vector3(x + 1, y + 1, z + 1), new Vector2(u0, v1)),
                (new Vector3(x + 1, y, z), new Vector2(u1, v0)),
                (new Vector3(x + 1, y + 1, z + 1), new Vector2(u0, v1)),
                (new Vector3(x + 1, y, z + 1), new Vector2(u0, v0))
            },
            Face.Left => new[]
            {
                (new Vector3(x, y, z + 1), new Vector2(u1, v0)),
                (new Vector3(x, y + 1, z + 1), new Vector2(u1, v1)),
                (new Vector3(x, y + 1, z), new Vector2(u0, v1)),
                (new Vector3(x, y, z + 1), new Vector2(u1, v0)),
                (new Vector3(x, y + 1, z), new Vector2(u0, v1)),
                (new Vector3(x, y, z), new Vector2(u0, v0))
            },
            _ => Array.Empty<(Vector3, Vector2)>()
        };

        foreach (var (pos, uv) in faceData)
        {
            verts.Add(pos.X);
            verts.Add(pos.Y);
            verts.Add(pos.Z);
            verts.Add(finalShade);
            verts.Add(finalShade);
            verts.Add(finalShade);
            verts.Add(normal.X);
            verts.Add(normal.Y);
            verts.Add(normal.Z);
            verts.Add(uv.X);
            verts.Add(uv.Y);
        }
    }

    private static void AddCross(List<float> verts, float x, float y, float z, BlockType block, int lightLevel)
    {
        float lightMultiplier = 0.1f + (lightLevel / (float)MAX_LIGHT) * 0.9f;
        float shade = 0.9f * lightMultiplier;

        var texCoords = BlockRegistry.GetSideTexture(block);
        float u0 = texCoords.TopLeft.X, v0 = texCoords.TopLeft.Y;
        float u1 = texCoords.BottomRight.X, v1 = texCoords.BottomRight.Y;

        Vector3 normal = Vector3.UnitY;

        (Vector3 pos, Vector2 uv)[][] quads =
        {
            new[]
            {
                (new Vector3(x, y, z), new Vector2(u0, v0)),
                (new Vector3(x + 1, y, z + 1), new Vector2(u1, v0)),
                (new Vector3(x + 1, y + 1, z + 1), new Vector2(u1, v1)),
                (new Vector3(x, y, z), new Vector2(u0, v0)),
                (new Vector3(x + 1, y + 1, z + 1), new Vector2(u1, v1)),
                (new Vector3(x, y + 1, z), new Vector2(u0, v1))
            },
            new[]
            {
                (new Vector3(x, y, z), new Vector2(u0, v0)),
                (new Vector3(x, y + 1, z), new Vector2(u0, v1)),
                (new Vector3(x + 1, y + 1, z + 1), new Vector2(u1, v1)),
                (new Vector3(x, y, z), new Vector2(u0, v0)),
                (new Vector3(x + 1, y + 1, z + 1), new Vector2(u1, v1)),
                (new Vector3(x + 1, y, z + 1), new Vector2(u1, v0))
            },
            new[]
            {
                (new Vector3(x + 1, y, z), new Vector2(u0, v0)),
                (new Vector3(x, y, z + 1), new Vector2(u1, v0)),
                (new Vector3(x, y + 1, z + 1), new Vector2(u1, v1)),
                (new Vector3(x + 1, y, z), new Vector2(u0, v0)),
                (new Vector3(x, y + 1, z + 1), new Vector2(u1, v1)),
                (new Vector3(x + 1, y + 1, z), new Vector2(u0, v1))
            },
            new[]
            {
                (new Vector3(x + 1, y, z), new Vector2(u0, v0)),
                (new Vector3(x + 1, y + 1, z), new Vector2(u0, v1)),
                (new Vector3(x, y + 1, z + 1), new Vector2(u1, v1)),
                (new Vector3(x + 1, y, z), new Vector2(u0, v0)),
                (new Vector3(x, y + 1, z + 1), new Vector2(u1, v1)),
                (new Vector3(x, y, z + 1), new Vector2(u1, v0))
            }
        };

        foreach (var quad in quads)
            foreach (var (pos, uv) in quad)
                AddVertex(verts, pos, shade, normal, uv);
    }

    private static void AddTorch(List<float> verts, float x, float y, float z, BlockType block, int lightLevel)
    {
        float light = 0.1f + (lightLevel / (float)MAX_LIGHT) * 0.9f;

        float x0 = x + TORCH_OFFSET, x1 = x + TORCH_OFFSET + TORCH_SIZE;
        float y0 = y, y1 = y + TORCH_HEIGHT;
        float z0 = z + TORCH_OFFSET, z1 = z + TORCH_OFFSET + TORCH_SIZE;

        var top = BlockRegistry.GetTopTexture(block);
        var bot = BlockRegistry.GetBottomTexture(block);
        var side = BlockRegistry.GetSideTexture(block);

        // Top
        float yTop = y1 - 0.062f;
        AddTorchQuad(verts, new[] {
            new Vector3(x0, yTop, z0), new Vector3(x0, yTop, z1),
            new Vector3(x1, yTop, z1), new Vector3(x1, yTop, z0)
        }, Vector3.UnitY, top, light);

        // Bottom
        AddTorchQuad(verts, new[] {
            new Vector3(x0, y0, z1), new Vector3(x0, y0, z0),
            new Vector3(x1, y0, z0), new Vector3(x1, y0, z1)
        }, -Vector3.UnitY, bot, 0.5f * light);

        // Front
        AddTorchQuad(verts, new[] {
            new Vector3(x0, y0, z1), new Vector3(x1, y0, z1),
            new Vector3(x1, y1, z1), new Vector3(x0, y1, z1)
        }, Vector3.UnitZ, side, 0.8f * light);

        // Back
        AddTorchQuad(verts, new[] {
            new Vector3(x1, y0, z0), new Vector3(x0, y0, z0),
            new Vector3(x0, y1, z0), new Vector3(x1, y1, z0)
        }, -Vector3.UnitZ, side, 0.8f * light);

        // Right
        AddTorchQuad(verts, new[] {
            new Vector3(x1, y0, z1), new Vector3(x1, y0, z0),
            new Vector3(x1, y1, z0), new Vector3(x1, y1, z1)
        }, Vector3.UnitX, side, 0.7f * light);

        // Left
        AddTorchQuad(verts, new[] {
            new Vector3(x0, y0, z0), new Vector3(x0, y0, z1),
            new Vector3(x0, y1, z1), new Vector3(x0, y1, z0)
        }, -Vector3.UnitX, side, 0.7f * light);
    }

    private static void AddTorchQuad(List<float> verts, Vector3[] c, Vector3 normal, TextureCoords tex, float shade)
    {
        float u0 = tex.TopLeft.X, v0 = tex.TopLeft.Y;
        float u1 = tex.BottomRight.X, v1 = tex.BottomRight.Y;

        AddVertex(verts, c[0], shade, normal, new Vector2(u0, v0));
        AddVertex(verts, c[1], shade, normal, new Vector2(u1, v0));
        AddVertex(verts, c[2], shade, normal, new Vector2(u1, v1));
        AddVertex(verts, c[0], shade, normal, new Vector2(u0, v0));
        AddVertex(verts, c[2], shade, normal, new Vector2(u1, v1));
        AddVertex(verts, c[3], shade, normal, new Vector2(u0, v1));
    }

    private static void AddVertex(List<float> verts, Vector3 pos, float shade, Vector3 normal, Vector2 uv)
    {
        verts.Add(pos.X);
        verts.Add(pos.Y);
        verts.Add(pos.Z);
        verts.Add(shade);
        verts.Add(shade);
        verts.Add(shade);
        verts.Add(normal.X);
        verts.Add(normal.Y);
        verts.Add(normal.Z);
        verts.Add(uv.X);
        verts.Add(uv.Y);
    }

    public void Render()
    {
        if (mVertexCount == 0)
            return;

        GL.BindVertexArray(mVao);
        GL.DrawArrays(PrimitiveType.Triangles, 0, mVertexCount);
    }

    public void Dispose()
    {
        if (mIsGpuInitialized)
        {
            GL.DeleteVertexArray(mVao);
            GL.DeleteBuffer(mVbo);
        }
    }
}