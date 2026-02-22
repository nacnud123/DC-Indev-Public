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
            Chunk.Face.Front => BlockRegistry.GetFrontTexture(block),
            Chunk.Face.Back => BlockRegistry.GetBackTexture(block),
            Chunk.Face.Left => BlockRegistry.GetLeftTexture(block),
            Chunk.Face.Right => BlockRegistry.GetRightTexture(block),
            _ => BlockRegistry.GetSideTexture(block)
        };

        AddFace(verts, x, y, z, face, texCoords, skyLight, blockLight, topY);
    }

    // Emit a face with explicit texture coords - no heap allocations, vertices written directly
    internal static void AddFace(List<float> verts, float x, float y, float z, Chunk.Face face, TextureCoords texCoords, int skyLight, int blockLight, float topY = -1f)
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

        float nx, ny, nz;
        switch (face)
        {
            case Chunk.Face.Front:
                nx = 0;
                ny = 0;
                nz = 1;
                AddVertex(verts, x, y, z + 1, skyLightNorm, blockLightNorm, faceShade, nx, ny, nz, u0, v0);
                AddVertex(verts, x + 1, top, z + 1, skyLightNorm, blockLightNorm, faceShade, nx, ny, nz, u1, v1);
                AddVertex(verts, x, top, z + 1, skyLightNorm, blockLightNorm, faceShade, nx, ny, nz, u0, v1);
                AddVertex(verts, x, y, z + 1, skyLightNorm, blockLightNorm, faceShade, nx, ny, nz, u0, v0);
                AddVertex(verts, x + 1, y, z + 1, skyLightNorm, blockLightNorm, faceShade, nx, ny, nz, u1, v0);
                AddVertex(verts, x + 1, top, z + 1, skyLightNorm, blockLightNorm, faceShade, nx, ny, nz, u1, v1);
                break;
            case Chunk.Face.Back:
                nx = 0;
                ny = 0;
                nz = -1;
                AddVertex(verts, x, y, z, skyLightNorm, blockLightNorm, faceShade, nx, ny, nz, u1, v0);
                AddVertex(verts, x, top, z, skyLightNorm, blockLightNorm, faceShade, nx, ny, nz, u1, v1);
                AddVertex(verts, x + 1, top, z, skyLightNorm, blockLightNorm, faceShade, nx, ny, nz, u0, v1);
                AddVertex(verts, x, y, z, skyLightNorm, blockLightNorm, faceShade, nx, ny, nz, u1, v0);
                AddVertex(verts, x + 1, top, z, skyLightNorm, blockLightNorm, faceShade, nx, ny, nz, u0, v1);
                AddVertex(verts, x + 1, y, z, skyLightNorm, blockLightNorm, faceShade, nx, ny, nz, u0, v0);
                break;
            case Chunk.Face.Top:
                nx = 0;
                ny = 1;
                nz = 0;
                AddVertex(verts, x, top, z, skyLightNorm, blockLightNorm, faceShade, nx, ny, nz, u0, v0);
                AddVertex(verts, x + 1, top, z + 1, skyLightNorm, blockLightNorm, faceShade, nx, ny, nz, u1, v1);
                AddVertex(verts, x + 1, top, z, skyLightNorm, blockLightNorm, faceShade, nx, ny, nz, u1, v0);
                AddVertex(verts, x, top, z, skyLightNorm, blockLightNorm, faceShade, nx, ny, nz, u0, v0);
                AddVertex(verts, x, top, z + 1, skyLightNorm, blockLightNorm, faceShade, nx, ny, nz, u0, v1);
                AddVertex(verts, x + 1, top, z + 1, skyLightNorm, blockLightNorm, faceShade, nx, ny, nz, u1, v1);
                break;
            case Chunk.Face.Bottom:
                nx = 0;
                ny = -1;
                nz = 0;
                AddVertex(verts, x, y, z, skyLightNorm, blockLightNorm, faceShade, nx, ny, nz, u0, v1);
                AddVertex(verts, x + 1, y, z, skyLightNorm, blockLightNorm, faceShade, nx, ny, nz, u1, v1);
                AddVertex(verts, x + 1, y, z + 1, skyLightNorm, blockLightNorm, faceShade, nx, ny, nz, u1, v0);
                AddVertex(verts, x, y, z, skyLightNorm, blockLightNorm, faceShade, nx, ny, nz, u0, v1);
                AddVertex(verts, x + 1, y, z + 1, skyLightNorm, blockLightNorm, faceShade, nx, ny, nz, u1, v0);
                AddVertex(verts, x, y, z + 1, skyLightNorm, blockLightNorm, faceShade, nx, ny, nz, u0, v0);
                break;
            case Chunk.Face.Right:
                nx = 1;
                ny = 0;
                nz = 0;
                AddVertex(verts, x + 1, y, z, skyLightNorm, blockLightNorm, faceShade, nx, ny, nz, u1, v0);
                AddVertex(verts, x + 1, top, z, skyLightNorm, blockLightNorm, faceShade, nx, ny, nz, u1, v1);
                AddVertex(verts, x + 1, top, z + 1, skyLightNorm, blockLightNorm, faceShade, nx, ny, nz, u0, v1);
                AddVertex(verts, x + 1, y, z, skyLightNorm, blockLightNorm, faceShade, nx, ny, nz, u1, v0);
                AddVertex(verts, x + 1, top, z + 1, skyLightNorm, blockLightNorm, faceShade, nx, ny, nz, u0, v1);
                AddVertex(verts, x + 1, y, z + 1, skyLightNorm, blockLightNorm, faceShade, nx, ny, nz, u0, v0);
                break;
            case Chunk.Face.Left:
                nx = -1;
                ny = 0;
                nz = 0;
                AddVertex(verts, x, y, z + 1, skyLightNorm, blockLightNorm, faceShade, nx, ny, nz, u1, v0);
                AddVertex(verts, x, top, z + 1, skyLightNorm, blockLightNorm, faceShade, nx, ny, nz, u1, v1);
                AddVertex(verts, x, top, z, skyLightNorm, blockLightNorm, faceShade, nx, ny, nz, u0, v1);
                AddVertex(verts, x, y, z + 1, skyLightNorm, blockLightNorm, faceShade, nx, ny, nz, u1, v0);
                AddVertex(verts, x, top, z, skyLightNorm, blockLightNorm, faceShade, nx, ny, nz, u0, v1);
                AddVertex(verts, x, y, z, skyLightNorm, blockLightNorm, faceShade, nx, ny, nz, u0, v0);
                break;
        }
    }

    // Four outer wall faces (no top/bottom) + two double-sided diagonal quads.
    internal static void AddFire(List<float> verts, float x, float y, float z, BlockType block, int skyLight, int blockLight)
    {
        float skyLightNorm = skyLight / (float)Chunk.MAX_LIGHT;
        float blockLightNorm = blockLight / (float)Chunk.MAX_LIGHT;

        var tex = BlockRegistry.GetSideTexture(block);
        float u0 = tex.TopLeft.X, v0 = tex.TopLeft.Y;
        float u1 = tex.BottomRight.X, v1 = tex.BottomRight.Y;

        // Outer wall faces

        // Front +Z
        AddVertex(verts, new Vector3(x, y, z + 1), skyLightNorm, blockLightNorm, 0.8f, Vector3.UnitZ, new Vector2(u0, v0));
        AddVertex(verts, new Vector3(x + 1, y + 1, z + 1), skyLightNorm, blockLightNorm, 0.8f, Vector3.UnitZ, new Vector2(u1, v1));
        AddVertex(verts, new Vector3(x, y + 1, z + 1), skyLightNorm, blockLightNorm, 0.8f, Vector3.UnitZ, new Vector2(u0, v1));
        AddVertex(verts, new Vector3(x, y, z + 1), skyLightNorm, blockLightNorm, 0.8f, Vector3.UnitZ, new Vector2(u0, v0));
        AddVertex(verts, new Vector3(x + 1, y, z + 1), skyLightNorm, blockLightNorm, 0.8f, Vector3.UnitZ, new Vector2(u1, v0));
        AddVertex(verts, new Vector3(x + 1, y + 1, z + 1), skyLightNorm, blockLightNorm, 0.8f, Vector3.UnitZ, new Vector2(u1, v1));

        // Back -Z
        AddVertex(verts, new Vector3(x, y, z), skyLightNorm, blockLightNorm, 0.8f, -Vector3.UnitZ, new Vector2(u1, v0));
        AddVertex(verts, new Vector3(x, y + 1, z), skyLightNorm, blockLightNorm, 0.8f, -Vector3.UnitZ, new Vector2(u1, v1));
        AddVertex(verts, new Vector3(x + 1, y + 1, z), skyLightNorm, blockLightNorm, 0.8f, -Vector3.UnitZ, new Vector2(u0, v1));
        AddVertex(verts, new Vector3(x, y, z), skyLightNorm, blockLightNorm, 0.8f, -Vector3.UnitZ, new Vector2(u1, v0));
        AddVertex(verts, new Vector3(x + 1, y + 1, z), skyLightNorm, blockLightNorm, 0.8f, -Vector3.UnitZ, new Vector2(u0, v1));
        AddVertex(verts, new Vector3(x + 1, y, z), skyLightNorm, blockLightNorm, 0.8f, -Vector3.UnitZ, new Vector2(u0, v0));

        // Right +X
        AddVertex(verts, new Vector3(x + 1, y, z), skyLightNorm, blockLightNorm, 0.7f, Vector3.UnitX, new Vector2(u1, v0));
        AddVertex(verts, new Vector3(x + 1, y + 1, z), skyLightNorm, blockLightNorm, 0.7f, Vector3.UnitX, new Vector2(u1, v1));
        AddVertex(verts, new Vector3(x + 1, y + 1, z + 1), skyLightNorm, blockLightNorm, 0.7f, Vector3.UnitX, new Vector2(u0, v1));
        AddVertex(verts, new Vector3(x + 1, y, z), skyLightNorm, blockLightNorm, 0.7f, Vector3.UnitX, new Vector2(u1, v0));
        AddVertex(verts, new Vector3(x + 1, y + 1, z + 1), skyLightNorm, blockLightNorm, 0.7f, Vector3.UnitX, new Vector2(u0, v1));
        AddVertex(verts, new Vector3(x + 1, y, z + 1), skyLightNorm, blockLightNorm, 0.7f, Vector3.UnitX, new Vector2(u0, v0));

        // Left -X
        AddVertex(verts, new Vector3(x, y, z + 1), skyLightNorm, blockLightNorm, 0.7f, -Vector3.UnitX, new Vector2(u1, v0));
        AddVertex(verts, new Vector3(x, y + 1, z + 1), skyLightNorm, blockLightNorm, 0.7f, -Vector3.UnitX, new Vector2(u1, v1));
        AddVertex(verts, new Vector3(x, y + 1, z), skyLightNorm, blockLightNorm, 0.7f, -Vector3.UnitX, new Vector2(u0, v1));
        AddVertex(verts, new Vector3(x, y, z + 1), skyLightNorm, blockLightNorm, 0.7f, -Vector3.UnitX, new Vector2(u1, v0));
        AddVertex(verts, new Vector3(x, y + 1, z), skyLightNorm, blockLightNorm, 0.7f, -Vector3.UnitX, new Vector2(u0, v1));
        AddVertex(verts, new Vector3(x, y, z), skyLightNorm, blockLightNorm, 0.7f, -Vector3.UnitX, new Vector2(u0, v0));

        // Interior cross quads (double-sided, shade 0.9)

        Vector3 crossNormal = Vector3.UnitY;
        float cs = 0.9f;

        // Diagonal A
        AddVertex(verts, new Vector3(x, y, z), skyLightNorm, blockLightNorm, cs, crossNormal, new Vector2(u0, v0));
        AddVertex(verts, new Vector3(x + 1, y, z + 1), skyLightNorm, blockLightNorm, cs, crossNormal, new Vector2(u1, v0));
        AddVertex(verts, new Vector3(x + 1, y + 1, z + 1), skyLightNorm, blockLightNorm, cs, crossNormal, new Vector2(u1, v1));
        AddVertex(verts, new Vector3(x, y, z), skyLightNorm, blockLightNorm, cs, crossNormal, new Vector2(u0, v0));
        AddVertex(verts, new Vector3(x + 1, y + 1, z + 1), skyLightNorm, blockLightNorm, cs, crossNormal, new Vector2(u1, v1));
        AddVertex(verts, new Vector3(x, y + 1, z), skyLightNorm, blockLightNorm, cs, crossNormal, new Vector2(u0, v1));

        AddVertex(verts, new Vector3(x, y, z), skyLightNorm, blockLightNorm, cs, crossNormal, new Vector2(u0, v0));
        AddVertex(verts, new Vector3(x, y + 1, z), skyLightNorm, blockLightNorm, cs, crossNormal, new Vector2(u0, v1));
        AddVertex(verts, new Vector3(x + 1, y + 1, z + 1), skyLightNorm, blockLightNorm, cs, crossNormal, new Vector2(u1, v1));
        AddVertex(verts, new Vector3(x, y, z), skyLightNorm, blockLightNorm, cs, crossNormal, new Vector2(u0, v0));
        AddVertex(verts, new Vector3(x + 1, y + 1, z + 1), skyLightNorm, blockLightNorm, cs, crossNormal, new Vector2(u1, v1));
        AddVertex(verts, new Vector3(x + 1, y, z + 1), skyLightNorm, blockLightNorm, cs, crossNormal, new Vector2(u1, v0));

        // Diagonal B
        AddVertex(verts, new Vector3(x + 1, y, z), skyLightNorm, blockLightNorm, cs, crossNormal, new Vector2(u0, v0));
        AddVertex(verts, new Vector3(x, y, z + 1), skyLightNorm, blockLightNorm, cs, crossNormal, new Vector2(u1, v0));
        AddVertex(verts, new Vector3(x, y + 1, z + 1), skyLightNorm, blockLightNorm, cs, crossNormal, new Vector2(u1, v1));
        AddVertex(verts, new Vector3(x + 1, y, z), skyLightNorm, blockLightNorm, cs, crossNormal, new Vector2(u0, v0));
        AddVertex(verts, new Vector3(x, y + 1, z + 1), skyLightNorm, blockLightNorm, cs, crossNormal, new Vector2(u1, v1));
        AddVertex(verts, new Vector3(x + 1, y + 1, z), skyLightNorm, blockLightNorm, cs, crossNormal, new Vector2(u0, v1));

        AddVertex(verts, new Vector3(x + 1, y, z), skyLightNorm, blockLightNorm, cs, crossNormal, new Vector2(u0, v0));
        AddVertex(verts, new Vector3(x + 1, y + 1, z), skyLightNorm, blockLightNorm, cs, crossNormal, new Vector2(u0, v1));
        AddVertex(verts, new Vector3(x, y + 1, z + 1), skyLightNorm, blockLightNorm, cs, crossNormal, new Vector2(u1, v1));
        AddVertex(verts, new Vector3(x + 1, y, z), skyLightNorm, blockLightNorm, cs, crossNormal, new Vector2(u0, v0));
        AddVertex(verts, new Vector3(x, y + 1, z + 1), skyLightNorm, blockLightNorm, cs, crossNormal, new Vector2(u1, v1));
        AddVertex(verts, new Vector3(x, y, z + 1), skyLightNorm, blockLightNorm, cs, crossNormal, new Vector2(u1, v0));
    }

    // Two diagonal quads forming an X shape. No face culling. Zero heap allocations.
    internal static void AddCross(List<float> verts, float x, float y, float z, BlockType block, int skyLight, int blockLight)
    {
        float sl = skyLight / (float)Chunk.MAX_LIGHT;
        float bl = blockLight / (float)Chunk.MAX_LIGHT;
        const float fs = 0.9f;

        // Normal points up for cross shapes
        const float nx = 0, ny = 1, nz = 0;

        var texCoords = BlockRegistry.GetSideTexture(block);
        float u0 = texCoords.TopLeft.X, v0 = texCoords.TopLeft.Y;
        float u1 = texCoords.BottomRight.X, v1 = texCoords.BottomRight.Y;

        float x1 = x + 1, y1 = y + 1, z1 = z + 1;

        // Diagonal A front
        AddVertex(verts, x, y, z, sl, bl, fs, nx, ny, nz, u0, v0);
        AddVertex(verts, x1, y, z1, sl, bl, fs, nx, ny, nz, u1, v0);
        AddVertex(verts, x1, y1, z1, sl, bl, fs, nx, ny, nz, u1, v1);
        AddVertex(verts, x, y, z, sl, bl, fs, nx, ny, nz, u0, v0);
        AddVertex(verts, x1, y1, z1, sl, bl, fs, nx, ny, nz, u1, v1);
        AddVertex(verts, x, y1, z, sl, bl, fs, nx, ny, nz, u0, v1);

        // Diagonal A back
        AddVertex(verts, x, y, z, sl, bl, fs, nx, ny, nz, u0, v0);
        AddVertex(verts, x, y1, z, sl, bl, fs, nx, ny, nz, u0, v1);
        AddVertex(verts, x1, y1, z1, sl, bl, fs, nx, ny, nz, u1, v1);
        AddVertex(verts, x, y, z, sl, bl, fs, nx, ny, nz, u0, v0);
        AddVertex(verts, x1, y1, z1, sl, bl, fs, nx, ny, nz, u1, v1);
        AddVertex(verts, x1, y, z1, sl, bl, fs, nx, ny, nz, u1, v0);

        // Diagonal B front
        AddVertex(verts, x1, y, z, sl, bl, fs, nx, ny, nz, u0, v0);
        AddVertex(verts, x, y, z1, sl, bl, fs, nx, ny, nz, u1, v0);
        AddVertex(verts, x, y1, z1, sl, bl, fs, nx, ny, nz, u1, v1);
        AddVertex(verts, x1, y, z, sl, bl, fs, nx, ny, nz, u0, v0);
        AddVertex(verts, x, y1, z1, sl, bl, fs, nx, ny, nz, u1, v1);
        AddVertex(verts, x1, y1, z, sl, bl, fs, nx, ny, nz, u0, v1);

        // Diagonal B back
        AddVertex(verts, x1, y, z, sl, bl, fs, nx, ny, nz, u0, v0);
        AddVertex(verts, x1, y1, z, sl, bl, fs, nx, ny, nz, u0, v1);
        AddVertex(verts, x, y1, z1, sl, bl, fs, nx, ny, nz, u1, v1);
        AddVertex(verts, x1, y, z, sl, bl, fs, nx, ny, nz, u0, v0);
        AddVertex(verts, x, y1, z1, sl, bl, fs, nx, ny, nz, u1, v1);
        AddVertex(verts, x, y, z1, sl, bl, fs, nx, ny, nz, u1, v0);
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
        [
            new(x0, y0, z0), new(x0, y0, z1), new(x1, y0, z0), new(x1, y0, z1),
            new(x0, y1, z0), new(x0, y1, z1), new(x1, y1, z0), new(x1, y1, z1)
        ];

        float yTop = y1 - 0.062f;
        Vector3[] topFace =
        [
            new(x0, yTop, z0), new(x0, yTop, z1), new(x1, yTop, z0), new(x1, yTop, z1)
        ];

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

        AddTorchQuad(verts, [topFace[0], topFace[1], topFace[3], topFace[2]], Vector3.UnitY, top, skyLightNorm, blockLightNorm, 1.0f);

        AddTorchQuad(verts, [corners[1], corners[0], corners[2], corners[3]], -Vector3.UnitY, bot, skyLightNorm, blockLightNorm, 0.5f);

        AddTorchQuad(verts, [corners[1], corners[3], corners[7], corners[5]], Vector3.UnitZ, side, skyLightNorm, blockLightNorm, 0.8f);

        AddTorchQuad(verts, [corners[2], corners[0], corners[4], corners[6]], -Vector3.UnitZ, side, skyLightNorm, blockLightNorm, 0.8f);

        AddTorchQuad(verts, [corners[3], corners[2], corners[6], corners[7]], Vector3.UnitX, side, skyLightNorm, blockLightNorm, 0.7f);

        AddTorchQuad(verts, [corners[0], corners[1], corners[5], corners[4]], -Vector3.UnitX, side, skyLightNorm, blockLightNorm, 0.7f);
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

    // Scalar overload avoids creating Vector3/Vector2 structs for the hot path
    private static void AddVertex(List<float> verts, float px, float py, float pz, float skyLightNorm, float blockLightNorm, float faceShade, float nx, float ny, float nz, float u, float v)
    {
        verts.Add(px);
        verts.Add(py);
        verts.Add(pz);
        verts.Add(skyLightNorm);
        verts.Add(blockLightNorm);
        verts.Add(faceShade);
        verts.Add(nx);
        verts.Add(ny);
        verts.Add(nz);
        verts.Add(u);
        verts.Add(v);
    }

    // Emits a quad (2 triangles) with UV scaled proportionally to box dimensions
    private static void EmitQuad(List<float> verts, Vector3 a, Vector3 b, Vector3 c, Vector3 d, Vector3 normal, float faceShade, float skyLightNorm, float blockLightNorm, TextureCoords tex, float uScale, float vScale)
    {
        float u0 = tex.TopLeft.X;
        float v0 = tex.TopLeft.Y;
        float u1 = u0 + (tex.BottomRight.X - u0) * uScale;
        float v1 = v0 + (tex.BottomRight.Y - v0) * vScale;

        // Two triangles: a-d-c and a-c-b (CCW winding when viewed from normal direction)
        AddVertex(verts, a, skyLightNorm, blockLightNorm, faceShade, normal, new Vector2(u0, v0));
        AddVertex(verts, d, skyLightNorm, blockLightNorm, faceShade, normal, new Vector2(u0, v1));
        AddVertex(verts, c, skyLightNorm, blockLightNorm, faceShade, normal, new Vector2(u1, v1));
        AddVertex(verts, a, skyLightNorm, blockLightNorm, faceShade, normal, new Vector2(u0, v0));
        AddVertex(verts, c, skyLightNorm, blockLightNorm, faceShade, normal, new Vector2(u1, v1));
        AddVertex(verts, b, skyLightNorm, blockLightNorm, faceShade, normal, new Vector2(u1, v0));
    }

    // Emits 6 faces for an arbitrary axis-aligned box
    internal static void AddStairBox(List<float> verts, float x0, float y0, float z0, float x1, float y1, float z1, BlockType block, int skyLight, int blockLight)
    {
        float skyLightNorm = skyLight / (float)Chunk.MAX_LIGHT;
        float blockLightNorm = blockLight / (float)Chunk.MAX_LIGHT;

        float xSize = x1 - x0;
        float ySize = y1 - y0;
        float zSize = z1 - z0;

        var top = BlockRegistry.GetTopTexture(block);
        var bot = BlockRegistry.GetBottomTexture(block);
        var front = BlockRegistry.GetFrontTexture(block);
        var back = BlockRegistry.GetBackTexture(block);
        var right = BlockRegistry.GetRightTexture(block);
        var left = BlockRegistry.GetLeftTexture(block);

        // Top face (shade 1.0)
        EmitQuad(verts,
            new Vector3(x0, y1, z0), new Vector3(x1, y1, z0),
            new Vector3(x1, y1, z1), new Vector3(x0, y1, z1),
            Vector3.UnitY, 1.0f, skyLightNorm, blockLightNorm, top, xSize, zSize);

        // Bottom face (shade 0.5)
        EmitQuad(verts,
            new Vector3(x0, y0, z1), new Vector3(x1, y0, z1),
            new Vector3(x1, y0, z0), new Vector3(x0, y0, z0),
            -Vector3.UnitY, 0.5f, skyLightNorm, blockLightNorm, bot, xSize, zSize);

        // Front face +Z (shade 0.8)
        EmitQuad(verts,
            new Vector3(x0, y0, z1), new Vector3(x0, y1, z1),
            new Vector3(x1, y1, z1), new Vector3(x1, y0, z1),
            Vector3.UnitZ, 0.8f, skyLightNorm, blockLightNorm, front, xSize, ySize);

        // Back face -Z (shade 0.8)
        EmitQuad(verts,
            new Vector3(x1, y0, z0), new Vector3(x1, y1, z0),
            new Vector3(x0, y1, z0), new Vector3(x0, y0, z0),
            -Vector3.UnitZ, 0.8f, skyLightNorm, blockLightNorm, back, xSize, ySize);

        // Right face +X (shade 0.7)
        EmitQuad(verts,
            new Vector3(x1, y0, z1), new Vector3(x1, y1, z1),
            new Vector3(x1, y1, z0), new Vector3(x1, y0, z0),
            Vector3.UnitX, 0.7f, skyLightNorm, blockLightNorm, right, zSize, ySize);

        // Left face -X (shade 0.7)
        EmitQuad(verts,
            new Vector3(x0, y0, z0), new Vector3(x0, y1, z0),
            new Vector3(x0, y1, z1), new Vector3(x0, y0, z1),
            -Vector3.UnitX, 0.7f, skyLightNorm, blockLightNorm, left, zSize, ySize);
    }
}
