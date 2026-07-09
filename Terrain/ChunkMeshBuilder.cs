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


using VoxelEngine.Rendering;
using VoxelEngine.Terrain.Blocks;

namespace VoxelEngine.Terrain;

/// <summary>
/// Pure vertex-emission helpers used while building a chunk's mesh. Each method appends raw floats (see the 11-float vertex layout documented at the top of this file) for one block's worth of geometry — a single face, a full box, a foliage cross, fire, or a torch — directly into the shared vertex buffer passed in. Face culling (deciding *whether* a face should be emitted, based on neighboring block opacity) happens in the caller (<c>Chunk</c>/mesh-rebuild code) before these methods are invoked; everything here assumes the face is already known to be visible and just worries about producing correct geometry, UVs, normals and per-vertex light/shade values. No methods here allocate on the heap in the hot per-face paths (the scalar <see cref="AddVertex(List{float},float,float,float,float,float,float,float,float,float,float,float)"/> overload exists specifically to avoid constructing Vector2/Vector3 per vertex).
/// </summary>
internal static class ChunkMeshBuilder
{
    // Torch model dimensions, all in block-fraction units (divided by 16, i.e. Minecraft's classic 16x16x16 pixel-grid convention for sub-block models).
    private const float TORCH_SIZE = 2f / 16f;    // torch pole cross-section width/depth
    private const float TORCH_HEIGHT = 10f / 16f; // torch pole height
    private const float TORCH_OFFSET = 7f / 16f;  // centers the pole within the block on X/Z
    // Wall-mounted torch tilt: sin/cos of a fixed ~22.5 degree lean angle used to rotate the torch model when attached to a wall (vs standing upright on the floor).
    private const float TILT_SIN = 0.3827f;
    private const float TILT_COS = 0.9239f;
    private const float WALL_SHIFT = 0.375f;      // horizontal offset pushing the torch out from the wall face
    private const float WALL_RAISE = 3f / 16f;    // vertical offset raising a wall torch off the mounting block's bottom
    private const float BASE_Y = 3.5f / 16f;      // pivot height used when rotating a wall torch into place

    /// <summary>
    /// Emits one face (2 triangles) of a standard cube block, looking up the correct atlas texture coordinates for the given face/block via <see cref="BlockRegistry"/>, then delegating to the explicit-texture overload below. <paramref name="topY"/> optionally overrides the face's top Y coordinate (used for partial-height blocks like slabs/liquids) instead of the full y+1. Face culling against neighbors must already have been decided by the caller.
    /// </summary>
    internal static void AddFace(List<float> verts, float x, float y, float z, Chunk.Face face, BlockType block,
        int skyLight, int blockLight, float topY = -1f)
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

    /// <summary>
    /// Emits one face (2 triangles / 6 vertices) of a standard cube using explicit atlas texture coordinates (skips the BlockRegistry lookup so callers with pre-resolved coords, or hot loops, avoid the extra indirection — no heap allocations, vertices written directly into <paramref name="verts"/>). <paramref name="faceShade"/> is a fixed baked-lighting factor per face direction (Top brightest at 1.0, Bottom darkest at 0.5, front/back 0.8, left/right 0.7) — a cheap directional-light approximation independent of the dynamic sky/block light values. Sky/block light bytes (0..Chunk.MAX_LIGHT) are normalized to [0,1] here for the shader.
    /// </summary>
    internal static void AddFace(List<float> verts, float x, float y, float z, Chunk.Face face, TextureCoords texCoords,
        int skyLight, int blockLight, float topY = -1f)
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

        // Each case below emits exactly 6 vertices (2 triangles sharing an edge) forming one 1x1 quad on the appropriate cube face, with a fixed outward-facing normal and counter-clockwise winding (as seen from outside the cube, matching the normal direction) so backface culling in the renderer keeps only the outward side visible.
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

    /// <summary>
    /// Builds Minecraft-style fire geometry as a cluster of double-sided quads. When the fire has a solid block base (<paramref name="hasSolidBase"/> true, e.g. fire sitting on a normal block), it draws the classic 8-quad "campfire" pattern of diagonal panels leaning slightly inward from the block's edges. When fire has no solid base (e.g. it's clinging to the side of a flammable block after spreading, sitting on grass/etc. that burned away), it instead draws leaning wall-panels only on the sides that are adjacent to a flammable neighbor (<paramref name="flammableNegX"/> etc.), plus an X-shaped cap quad pair on top when the block above is flammable — mirroring how vanilla fire clings to whatever fuel is actually present.
    /// </summary>
    internal static void AddFire(List<float> verts, float x, float y, float z, BlockType block, int skyLight,
        int blockLight,
        bool hasSolidBase, bool flammableNegX, bool flammablePosX, bool flammableNegZ, bool flammablePosZ,
        bool flammableAbove)
    {
        float sl = skyLight / (float)Chunk.MAX_LIGHT;
        float bl = blockLight / (float)Chunk.MAX_LIGHT;
        const float shade = 0.9f;
        const float fireHeight = 1.4f;
        const float yBase = 1f / 16f; // raise bottom edge 1/16 off ground

        var tex = BlockRegistry.GetSideTexture(block);
        float u0 = tex.TopLeft.X, v0 = tex.TopLeft.Y;
        float u1 = tex.BottomRight.X, v1 = tex.BottomRight.Y;

        if (!hasSolidBase)
        {
            float yBot = y + yBase;
            float yTop = y + fireHeight + yBase;

            // -X neighbor
            if (flammableNegX)
            {
                // Bottom edge at x, top edge at x+0.2 (leans inward)
                AddFireQuadDoubleSided(verts, sl, bl, shade,
                    new Vector3(x, yBot, z), new Vector3(x, yBot, z + 1),
                    new Vector3(x + 0.2f, yTop, z + 1), new Vector3(x + 0.2f, yTop, z),
                    u0, v0, u1, v1);
            }

            // +X neighbor
            if (flammablePosX)
            {
                // Bottom edge at x+1, top edge at x+0.8 (leans inward)
                AddFireQuadDoubleSided(verts, sl, bl, shade,
                    new Vector3(x + 1, yBot, z + 1), new Vector3(x + 1, yBot, z),
                    new Vector3(x + 0.8f, yTop, z), new Vector3(x + 0.8f, yTop, z + 1),
                    u0, v0, u1, v1);
            }

            // -Z neighbor
            if (flammableNegZ)
            {
                // Bottom edge at z, top edge at z+0.2
                AddFireQuadDoubleSided(verts, sl, bl, shade,
                    new Vector3(x + 1, yBot, z), new Vector3(x, yBot, z),
                    new Vector3(x, yTop, z + 0.2f), new Vector3(x + 1, yTop, z + 0.2f),
                    u0, v0, u1, v1);
            }

            // +Z neighbor
            if (flammablePosZ)
            {
                // Bottom edge at z+1, top edge at z+0.8
                AddFireQuadDoubleSided(verts, sl, bl, shade,
                    new Vector3(x, yBot, z + 1), new Vector3(x + 1, yBot, z + 1),
                    new Vector3(x + 1, yTop, z + 0.8f), new Vector3(x, yTop, z + 0.8f),
                    u0, v0, u1, v1);
            }

            // Above neighbor - X-cap
            if (flammableAbove)
            {
                AddFireXCap(verts, x, z, y, sl, bl, shade, u0, v0, u1, v1);
            }
        }
        else
        {
            // 8 diagonal leaning quads (classic campfire pillars)
            AddFireQuadDoubleSided(verts, sl, bl, shade,
                new Vector3(x + 0.7f, y, z),
                new Vector3(x + 0.7f, y, z + 1),
                new Vector3(x + 0.2f, y + fireHeight, z + 1),
                new Vector3(x + 0.2f, y + fireHeight, z),
                u0, v0, u1, v1);
            // Quad leans right: top at x+0.8, bottom at x+0.3
            AddFireQuadDoubleSided(verts, sl, bl, shade,
                new Vector3(x + 0.3f, y, z + 1),
                new Vector3(x + 0.3f, y, z),
                new Vector3(x + 0.8f, y + fireHeight, z),
                new Vector3(x + 0.8f, y + fireHeight, z + 1),
                u0, v0, u1, v1);

            // Z-axis pair Quad leans forward: top at z+0.2, bottom at z+0.7
            AddFireQuadDoubleSided(verts, sl, bl, shade,
                new Vector3(x, y, z + 0.7f),
                new Vector3(x + 1, y, z + 0.7f),
                new Vector3(x + 1, y + fireHeight, z + 0.2f),
                new Vector3(x, y + fireHeight, z + 0.2f),
                u0, v0, u1, v1);
            // Quad leans back: top at z+0.8, bottom at z+0.3
            AddFireQuadDoubleSided(verts, sl, bl, shade,
                new Vector3(x + 1, y, z + 0.3f),
                new Vector3(x, y, z + 0.3f),
                new Vector3(x, y + fireHeight, z + 0.8f),
                new Vector3(x + 1, y + fireHeight, z + 0.8f),
                u0, v0, u1, v1);

            // Wide panels near block edges, slight lean inward X-axis outer pair
            AddFireQuadDoubleSided(verts, sl, bl, shade,
                new Vector3(x, y, z),
                new Vector3(x, y, z + 1),
                new Vector3(x + 0.1f, y + fireHeight, z + 1),
                new Vector3(x + 0.1f, y + fireHeight, z),
                u0, v0, u1, v1);
            AddFireQuadDoubleSided(verts, sl, bl, shade,
                new Vector3(x + 1, y, z + 1),
                new Vector3(x + 1, y, z),
                new Vector3(x + 0.9f, y + fireHeight, z),
                new Vector3(x + 0.9f, y + fireHeight, z + 1),
                u0, v0, u1, v1);

            // Z-axis outer pair
            AddFireQuadDoubleSided(verts, sl, bl, shade,
                new Vector3(x + 1, y, z),
                new Vector3(x, y, z),
                new Vector3(x, y + fireHeight, z + 0.1f),
                new Vector3(x + 1, y + fireHeight, z + 0.1f),
                u0, v0, u1, v1);
            AddFireQuadDoubleSided(verts, sl, bl, shade,
                new Vector3(x, y, z + 1),
                new Vector3(x + 1, y, z + 1),
                new Vector3(x + 1, y + fireHeight, z + 0.9f),
                new Vector3(x, y + fireHeight, z + 0.9f),
                u0, v0, u1, v1);
        }
    }

    /// <summary>
    /// Emits a fire panel as two triangle pairs — one winding for the front face, one with reversed winding for the back face — so the flat quad is visible from both sides (fire has no back-face culling benefit since it's a thin plane, unlike a solid cube face).
    /// </summary>
    private static void AddFireQuadDoubleSided(List<float> verts, float sl, float bl2, float shade,
        Vector3 bottomLeft, Vector3 bottomRight, Vector3 topRight, Vector3 topLeft,
        float u0, float v0, float u1, float v1)
    {
        Vector3 n = Vector3.UnitY; // fire quads use upward normal for lighting

        // Front face
        AddVertex(verts, bottomLeft, sl, bl2, shade, n, new Vector2(u0, v0));
        AddVertex(verts, bottomRight, sl, bl2, shade, n, new Vector2(u1, v0));
        AddVertex(verts, topRight, sl, bl2, shade, n, new Vector2(u1, v1));
        AddVertex(verts, bottomLeft, sl, bl2, shade, n, new Vector2(u0, v0));
        AddVertex(verts, topRight, sl, bl2, shade, n, new Vector2(u1, v1));
        AddVertex(verts, topLeft, sl, bl2, shade, n, new Vector2(u0, v1));

        // Back face (reversed winding)
        AddVertex(verts, bottomLeft, sl, bl2, shade, n, new Vector2(u0, v0));
        AddVertex(verts, topLeft, sl, bl2, shade, n, new Vector2(u0, v1));
        AddVertex(verts, topRight, sl, bl2, shade, n, new Vector2(u1, v1));
        AddVertex(verts, bottomLeft, sl, bl2, shade, n, new Vector2(u0, v0));
        AddVertex(verts, topRight, sl, bl2, shade, n, new Vector2(u1, v1));
        AddVertex(verts, bottomRight, sl, bl2, shade, n, new Vector2(u1, v0));
    }

    /// <summary>Two double-sided diagonal quads forming an X, used as a top-cap when fire has no solid base beneath it but a flammable block above.</summary>
    private static void AddFireXCap(List<float> verts, float x, float z, float y, float sl, float bl, float shade,
        float u0, float v0, float u1, float v1)
    {
        float yTop = y + 1.4f + 1f / 16f;
        float yBot = y + 1f / 16f;

        // Diagonal A: (x,z) to (x+1,z+1)
        AddFireQuadDoubleSided(verts, sl, bl, shade,
            new Vector3(x, yBot, z),
            new Vector3(x + 1, yBot, z + 1),
            new Vector3(x + 1, yTop, z + 1),
            new Vector3(x, yTop, z),
            u0, v0, u1, v1);

        // Diagonal B: (x+1,z) to (x,z+1)
        AddFireQuadDoubleSided(verts, sl, bl, shade,
            new Vector3(x + 1, yBot, z),
            new Vector3(x, yBot, z + 1),
            new Vector3(x, yTop, z + 1),
            new Vector3(x + 1, yTop, z),
            u0, v0, u1, v1);
    }

    /// <summary>
    /// Builds foliage-style geometry (grass, flowers, saplings, etc.) as two intersecting diagonal quads forming an X when viewed from above, each rendered double-sided (front+back triangle sets) so the sprite is visible from any horizontal angle. Unlike cube faces, cross blocks are never culled against neighbors — they always render fully regardless of what's adjacent. No heap allocations (uses the scalar AddVertex overload throughout).
    /// </summary>
    internal static void AddCross(List<float> verts, float x, float y, float z, BlockType block, int skyLight,
        int blockLight)
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

    /// <summary>
    /// Builds a torch as a thin vertical box (pole) sized/offset by the TORCH_* constants so it sits centered and short on top of its block. When <paramref name="facing"/> is -1 the torch stands upright on the floor as-is; when facing is 0-3 (one of the 4 cardinal wall directions) every corner is rotated via <see cref="RotateWallTorch"/> to tilt the pole outward from a wall mount, matching vanilla wall-torch placement. The 6 box faces are emitted individually with their own per-face shade constant (same top/bottom/side shading scheme as full cube faces).
    /// </summary>
    internal static void AddTorch(List<float> verts, float x, float y, float z, BlockType block, int skyLight,
        int blockLight, int facing = -1)
    {
        float skyLightNorm = skyLight / (float)Chunk.MAX_LIGHT;
        float blockLightNorm = blockLight / (float)Chunk.MAX_LIGHT;

        float x0 = x + TORCH_OFFSET, x1 = x + TORCH_OFFSET + TORCH_SIZE;
        float y0 = y, y1 = y + TORCH_HEIGHT;
        float z0 = z + TORCH_OFFSET, z1 = z + TORCH_OFFSET + TORCH_SIZE;

        var top = BlockRegistry.GetTopTexture(block);
        var bot = BlockRegistry.GetBottomTexture(block);
        var side = BlockRegistry.GetSideTexture(block);

        // The 8 corners of the torch's thin bounding box, in a fixed index order so the quad emission calls below (corners[i]) can pick out the 4 corners of each face by index.
        Vector3[] corners =
        [
            new(x0, y0, z0), new(x0, y0, z1), new(x1, y0, z0), new(x1, y0, z1),
            new(x0, y1, z0), new(x0, y1, z1), new(x1, y1, z0), new(x1, y1, z1)
        ];

        // The top face is drawn slightly recessed below the pole's true top (y1) to mimic the torch's flame/tip visually sitting inside the pole rather than flush with its top edge.
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

        AddTorchQuad(verts, [topFace[0], topFace[1], topFace[3], topFace[2]], Vector3.UnitY, top, skyLightNorm,
            blockLightNorm, 1.0f);

        AddTorchQuad(verts, [corners[1], corners[0], corners[2], corners[3]], -Vector3.UnitY, bot, skyLightNorm,
            blockLightNorm, 0.5f);

        AddTorchQuad(verts, [corners[1], corners[3], corners[7], corners[5]], Vector3.UnitZ, side, skyLightNorm,
            blockLightNorm, 0.8f);

        AddTorchQuad(verts, [corners[2], corners[0], corners[4], corners[6]], -Vector3.UnitZ, side, skyLightNorm,
            blockLightNorm, 0.8f);

        AddTorchQuad(verts, [corners[3], corners[2], corners[6], corners[7]], Vector3.UnitX, side, skyLightNorm,
            blockLightNorm, 0.7f);

        AddTorchQuad(verts, [corners[0], corners[1], corners[5], corners[4]], -Vector3.UnitX, side, skyLightNorm,
            blockLightNorm, 0.7f);
    }

    /// <summary>
    /// Rotates a torch-model point about a pivot to tilt it outward from a wall for the given cardinal <paramref name="facing"/> (0-3). Translates the point into pivot-local space, applies a fixed-angle 2D rotation (using the precomputed TILT_SIN/TILT_COS) in the plane perpendicular to the wall, then re-adds the pivot and a constant <see cref="WALL_SHIFT"/> push away from the wall face so the tilted pole doesn't clip into the mounting block. Facings outside 0-3 return the point unchanged (defensive default).
    /// </summary>
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
    private static void AddVertex(List<float> verts, float px, float py, float pz, float skyLightNorm,
        float blockLightNorm, float faceShade, float nx, float ny, float nz, float u, float v)
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
    private static void EmitQuad(List<float> verts, Vector3 a, Vector3 b, Vector3 c, Vector3 d, Vector3 normal,
        float faceShade, float skyLightNorm, float blockLightNorm, TextureCoords tex, float uScale, float vScale)
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
    internal static void AddStairBox(List<float> verts, float x0, float y0, float z0, float x1, float y1, float z1,
        BlockType block, int skyLight, int blockLight)
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