// Main chunk mesh building class. It builds the chunk's mesh. | DA | 2/14/26
/*Vertex Format
 *Each vertex is 11 floats (44 bytes):
   Offset 	Size 	Data
   0-2 	3 floats 	Position (x, y, z)
   3 	1 float 	Sky light (0.0 - 1.0, normalized from 0-15)
   4 	1 float 	Block light (0.0 - 1.0, normalized from 0-15)
   5 	1 float 	Face shade (0.5 - 1.0)
   6-8 	3 floats 	Normal vector
   9-10 	2 floats 	Texture UV coordinates
 * 
 */
using OpenTK.Mathematics;
using VoxelEngine.Rendering;
using VoxelEngine.Terrain.Blocks;

namespace VoxelEngine.Terrain;

internal static class ChunkMeshBuilder
{
    private const float TORCH_SIZE = 2f / 16f;
    private const float TORCH_HEIGHT = 10f / 16f;
    private const float TORCH_OFFSET = 7f / 16f;
    private const float TILT_SIN = 0.3827f;
    private const float TILT_COS = 0.9239f;
    private const float WALL_SHIFT = 0.375f;
    private const float WALL_RAISE = 3f / 16f;
    private const float BASE_Y = 3.5f / 16f;

    // Normal block building, 6 faces. Check's neighboring blocks for face culling.
    internal static void AddFace(List<float> verts, float x, float y, float z, Chunk.Face face, BlockType block, int skyLight, int blockLight, float topY = -1f)
    {
        float top = topY < 0 ? y + 1 : topY;

        float faceShade = face switch
        {
            Chunk.Face.Top => 1.0f,
            Chunk.Face.Bottom => 0.5f,
            Chunk.Face.Front or Chunk.Face.Back => 0.8f,
            _ => 0.7f
        };

        float skyLightNorm = skyLight / (float)Chunk.MAX_LIGHT;
        float blockLightNorm = blockLight / (float)Chunk.MAX_LIGHT;

        var texCoords = face switch
        {
            Chunk.Face.Top => BlockRegistry.GetTopTexture(block),
            Chunk.Face.Bottom => BlockRegistry.GetBottomTexture(block),
            _ => BlockRegistry.GetSideTexture(block)
        };

        float u0 = texCoords.TopLeft.X;
        float v0 = texCoords.TopLeft.Y;
        float u1 = texCoords.BottomRight.X;
        float v1 = texCoords.BottomRight.Y;

        // Scale side UVs to match actual height when topY is explicitly set
        if (topY >= 0 && face != Chunk.Face.Top && face != Chunk.Face.Bottom)
        {
            float heightRatio = top - y;
            v1 = v0 + (v1 - v0) * heightRatio;
        }

        Vector3 normal = face switch
        {
            Chunk.Face.Front => Vector3.UnitZ,
            Chunk.Face.Back => -Vector3.UnitZ,
            Chunk.Face.Top => Vector3.UnitY,
            Chunk.Face.Bottom => -Vector3.UnitY,
            Chunk.Face.Right => Vector3.UnitX,
            Chunk.Face.Left => -Vector3.UnitX,
            _ => Vector3.Zero
        };

        (Vector3 pos, Vector2 uv)[] faceData = face switch
        {
            Chunk.Face.Front => new[]
            {
                (new Vector3(x, y, z + 1), new Vector2(u0, v0)),
                (new Vector3(x + 1, top, z + 1), new Vector2(u1, v1)),
                (new Vector3(x, top, z + 1), new Vector2(u0, v1)),
                (new Vector3(x, y, z + 1), new Vector2(u0, v0)),
                (new Vector3(x + 1, y, z + 1), new Vector2(u1, v0)),
                (new Vector3(x + 1, top, z + 1), new Vector2(u1, v1))
            },
            Chunk.Face.Back => new[]
            {
                (new Vector3(x, y, z), new Vector2(u1, v0)),
                (new Vector3(x, top, z), new Vector2(u1, v1)),
                (new Vector3(x + 1, top, z), new Vector2(u0, v1)),
                (new Vector3(x, y, z), new Vector2(u1, v0)),
                (new Vector3(x + 1, top, z), new Vector2(u0, v1)),
                (new Vector3(x + 1, y, z), new Vector2(u0, v0))
            },
            Chunk.Face.Top => new[]
            {
                (new Vector3(x, top, z), new Vector2(u0, v0)),
                (new Vector3(x + 1, top, z + 1), new Vector2(u1, v1)),
                (new Vector3(x + 1, top, z), new Vector2(u1, v0)),
                (new Vector3(x, top, z), new Vector2(u0, v0)),
                (new Vector3(x, top, z + 1), new Vector2(u0, v1)),
                (new Vector3(x + 1, top, z + 1), new Vector2(u1, v1))
            },
            Chunk.Face.Bottom => new[]
            {
                (new Vector3(x, y, z), new Vector2(u0, v1)),
                (new Vector3(x + 1, y, z), new Vector2(u1, v1)),
                (new Vector3(x + 1, y, z + 1), new Vector2(u1, v0)),
                (new Vector3(x, y, z), new Vector2(u0, v1)),
                (new Vector3(x + 1, y, z + 1), new Vector2(u1, v0)),
                (new Vector3(x, y, z + 1), new Vector2(u0, v0))
            },
            Chunk.Face.Right => new[]
            {
                (new Vector3(x + 1, y, z), new Vector2(u1, v0)),
                (new Vector3(x + 1, top, z), new Vector2(u1, v1)),
                (new Vector3(x + 1, top, z + 1), new Vector2(u0, v1)),
                (new Vector3(x + 1, y, z), new Vector2(u1, v0)),
                (new Vector3(x + 1, top, z + 1), new Vector2(u0, v1)),
                (new Vector3(x + 1, y, z + 1), new Vector2(u0, v0))
            },
            Chunk.Face.Left => new[]
            {
                (new Vector3(x, y, z + 1), new Vector2(u1, v0)),
                (new Vector3(x, top, z + 1), new Vector2(u1, v1)),
                (new Vector3(x, top, z), new Vector2(u0, v1)),
                (new Vector3(x, y, z + 1), new Vector2(u1, v0)),
                (new Vector3(x, top, z), new Vector2(u0, v1)),
                (new Vector3(x, y, z), new Vector2(u0, v0))
            },
            _ => Array.Empty<(Vector3, Vector2)>()
        };

        foreach (var (pos, uv) in faceData)
        {
            verts.Add(pos.X);
            verts.Add(pos.Y);
            verts.Add(pos.Z);
            verts.Add(skyLightNorm);
            verts.Add(blockLightNorm);
            verts.Add(faceShade);
            verts.Add(normal.X);
            verts.Add(normal.Y);
            verts.Add(normal.Z);
            verts.Add(uv.X);
            verts.Add(uv.Y);
        }
    }

    // Two diagonal quads forming an X shape. No face culling.
    internal static void AddCross(List<float> verts, float x, float y, float z, BlockType block, int skyLight, int blockLight)
    {
        float skyLightNorm = skyLight / (float)Chunk.MAX_LIGHT;
        float blockLightNorm = blockLight / (float)Chunk.MAX_LIGHT;
        float faceShade = 0.9f;

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
                AddVertex(verts, pos, skyLightNorm, blockLightNorm, faceShade, normal, uv);
    }

    // Custom block building made for torches, also does mesh building for wall mounted torches
    internal static void AddTorch(List<float> verts, float x, float y, float z, BlockType block, int skyLight, int blockLight, int facing = -1)
    {
        float skyLightNorm = skyLight / (float)Chunk.MAX_LIGHT;
        float blockLightNorm = blockLight / (float)Chunk.MAX_LIGHT;

        float x0 = x + TORCH_OFFSET, x1 = x + TORCH_OFFSET + TORCH_SIZE;
        float y0 = y, y1 = y + TORCH_HEIGHT;
        float z0 = z + TORCH_OFFSET, z1 = z + TORCH_OFFSET + TORCH_SIZE;

        var top = BlockRegistry.GetTopTexture(block);
        var bot = BlockRegistry.GetBottomTexture(block);
        var side = BlockRegistry.GetSideTexture(block);

        Vector3[] corners =
        {
            new(x0, y0, z0), new(x0, y0, z1), new(x1, y0, z0), new(x1, y0, z1),
            new(x0, y1, z0), new(x0, y1, z1), new(x1, y1, z0), new(x1, y1, z1)
        };

        float yTop = y1 - 0.062f;
        Vector3[] topFace =
        {
            new(x0, yTop, z0), new(x0, yTop, z1), new(x1, yTop, z0), new(x1, yTop, z1)
        };

        if (facing >= 0)
        {
            float pivotX = x + 0.5f;
            float pivotZ = z + 0.5f;
            float pivotY = y + BASE_Y;

            var raise = new Vector3(0, WALL_RAISE, 0);
            for (int i = 0; i < corners.Length; i++)
                corners[i] = RotateWallTorch(corners[i], pivotX, pivotY, pivotZ, facing) + raise;
            for (int i = 0; i < topFace.Length; i++)
                topFace[i] = RotateWallTorch(topFace[i], pivotX, pivotY, pivotZ, facing) + raise;
        }

        AddTorchQuad(verts, new[] { topFace[0], topFace[1], topFace[3], topFace[2] },
            Vector3.UnitY, top, skyLightNorm, blockLightNorm, 1.0f);

        AddTorchQuad(verts, new[] { corners[1], corners[0], corners[2], corners[3] },
            -Vector3.UnitY, bot, skyLightNorm, blockLightNorm, 0.5f);

        AddTorchQuad(verts, new[] { corners[1], corners[3], corners[7], corners[5] },
            Vector3.UnitZ, side, skyLightNorm, blockLightNorm, 0.8f);

        AddTorchQuad(verts, new[] { corners[2], corners[0], corners[4], corners[6] },
            -Vector3.UnitZ, side, skyLightNorm, blockLightNorm, 0.8f);

        AddTorchQuad(verts, new[] { corners[3], corners[2], corners[6], corners[7] },
            Vector3.UnitX, side, skyLightNorm, blockLightNorm, 0.7f);

        AddTorchQuad(verts, new[] { corners[0], corners[1], corners[5], corners[4] },
            -Vector3.UnitX, side, skyLightNorm, blockLightNorm, 0.7f);
    }

    private static Vector3 RotateWallTorch(Vector3 point, float pivotX, float pivotY, float pivotZ, int facing)
    {
        float lx = point.X - pivotX;
        float ly = point.Y - pivotY;
        float lz = point.Z - pivotZ;

        return facing switch
        {
            0 => new Vector3(pivotX + lx, pivotY + ly * TILT_COS - lz * TILT_SIN,
                pivotZ + ly * TILT_SIN + lz * TILT_COS - WALL_SHIFT),
            1 => new Vector3(pivotX + lx, pivotY + ly * TILT_COS + lz * TILT_SIN,
                pivotZ - ly * TILT_SIN + lz * TILT_COS + WALL_SHIFT),
            2 => new Vector3(pivotX - ly * TILT_SIN + lx * TILT_COS + WALL_SHIFT,
                pivotY + ly * TILT_COS + lx * TILT_SIN, pivotZ + lz),
            3 => new Vector3(pivotX + ly * TILT_SIN + lx * TILT_COS - WALL_SHIFT,
                pivotY + ly * TILT_COS - lx * TILT_SIN, pivotZ + lz),
            _ => point
        };
    }

    private static void AddTorchQuad(List<float> verts, Vector3[] c, Vector3 normal, TextureCoords tex,
        float skyLightNorm, float blockLightNorm, float faceShade)
    {
        float u0 = tex.TopLeft.X, v0 = tex.TopLeft.Y;
        float u1 = tex.BottomRight.X, v1 = tex.BottomRight.Y;

        AddVertex(verts, c[0], skyLightNorm, blockLightNorm, faceShade, normal, new Vector2(u0, v0));
        AddVertex(verts, c[1], skyLightNorm, blockLightNorm, faceShade, normal, new Vector2(u1, v0));
        AddVertex(verts, c[2], skyLightNorm, blockLightNorm, faceShade, normal, new Vector2(u1, v1));
        AddVertex(verts, c[0], skyLightNorm, blockLightNorm, faceShade, normal, new Vector2(u0, v0));
        AddVertex(verts, c[2], skyLightNorm, blockLightNorm, faceShade, normal, new Vector2(u1, v1));
        AddVertex(verts, c[3], skyLightNorm, blockLightNorm, faceShade, normal, new Vector2(u0, v1));
    }

    private static void AddVertex(List<float> verts, Vector3 pos, float skyLightNorm, float blockLightNorm,
        float faceShade, Vector3 normal, Vector2 uv)
    {
        verts.Add(pos.X);
        verts.Add(pos.Y);
        verts.Add(pos.Z);
        verts.Add(skyLightNorm);
        verts.Add(blockLightNorm);
        verts.Add(faceShade);
        verts.Add(normal.X);
        verts.Add(normal.Y);
        verts.Add(normal.Z);
        verts.Add(uv.X);
        verts.Add(uv.Y);
    }
}
