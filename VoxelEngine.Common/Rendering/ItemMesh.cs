// Vertex data for an item/block shown as an object in the world - dropped on the ground, or held in
// a player's hand. | Stage 7

using VoxelEngine.Items;
using VoxelEngine.Terrain;
using VoxelEngine.Terrain.Blocks;
using VoxelEngine.Utils;

namespace VoxelEngine.Rendering;

/// <summary>
/// Builds the little mesh that represents a stack outside the inventory. Cube-like blocks get a
/// small 3D cube from their own face textures; everything else gets a flat billboard quad from the
/// inventory icon. Callers own the upload, so lifetimes stay theirs.
/// </summary>
public static class ItemMesh
{
    /// <summary>Position (3) + UV (2) + normal (3).</summary>
    public const int VERTEX_STRIDE = 8;

    /// <summary>Half the depth (in blocks) a thick-sprite item is extruded to. See BuildThickSprite.</summary>
    private const float THICK_DEPTH = 2f / 16f;

    /// <summary>True when this stack renders as a 3D cube rather than an extruded icon.</summary>
    public static bool IsCube(ItemStack stack)
    {
        if (!stack.IsBlock)
            return false;

        var renderType = BlockRegistry.GetRenderType(stack.Block);
        return renderType is RenderingType.Normal or RenderingType.Slab or RenderingType.Stair;
    }

    /// <summary>A cube for block shapes, otherwise a thick sprite extruded from the inventory icon
    /// (empty if the atlas alpha isn't loaded yet, e.g. on a headless server).</summary>
    public static float[] Build(ItemStack stack)
    {
        if (IsCube(stack))
            return BuildCube(stack);

        var atlas = RenderBackend.Current.GetAtlasAlpha(itemAtlas: !stack.IsBlock);
        if (atlas == null)
            return Array.Empty<float>();

        var uv = stack.IsBlock
            ? BlockRegistry.Get(stack.Block).InventoryTextureCoords
            : ItemRegistry.GetItemCoords(stack.Item);

        return BuildThickSprite(uv, atlas.Value);
    }

    // Stairs are approximated as two stacked half-height boxes rather than true L-shaped geometry -
    // it's a small icon and the silhouette doesn't need to be exact.
    private static float[] BuildCube(ItemStack stack)
    {
        var block = BlockRegistry.Get(stack.Block);
        var verts = new List<float>();

        if (block.RenderType == RenderingType.Stair)
        {
            AddBox(verts, block, new Vector3(0, 0, 0), new Vector3(1, 0.5f, 1));
            AddBox(verts, block, new Vector3(0, 0.5f, 0), new Vector3(1, 1f, 0.5f));
        }
        else
        {
            AddBox(verts, block, block.BoundsMin, block.BoundsMax);
        }

        return verts.ToArray();
    }

    /// <summary>Extrudes an inventory icon into a thin box, one small cube per opaque pixel, like
    /// PlayerArm's first-person held item. Spans 0..1 in X/Y like BuildCube.</summary>
    public static float[] BuildThickSprite(TextureCoords tileTex, (byte[,] Alpha, int TilePixels) atlas)
    {
        var (alpha, tp) = atlas;
        var verts = new List<float>();
        float ps = 1f / tp;
        float hd = THICK_DEPTH * 0.5f;
        float tps = (tileTex.BottomRight.X - tileTex.TopLeft.X) / tp;

        float uBase = tileTex.TopLeft.X;
        float vBase = tileTex.TopLeft.Y;

        for (int py = 0; py < tp; py++)
        {
            for (int px = 0; px < tp; px++)
            {
                int atlasCol = (int)(uBase / (1f / UvHelper.TILE_COUNT) * tp) + px;
                int atlasRow = (int)(vBase / (1f / UvHelper.TILE_COUNT) * tp) + py;

                if (alpha[atlasCol, atlasRow] < 10)
                    continue;

                float x0 = px * ps, x1 = (px + 1) * ps;
                float y0 = py * ps, y1 = (py + 1) * ps;
                float z0 = -hd, z1 = hd;

                float u = uBase + (px + 0.5f) * tps;
                float v = vBase + (py + 0.5f) * tps;

                bool hasLeft = px > 0 && alpha[atlasCol - 1, atlasRow] >= 10;
                bool hasRight = px < tp - 1 && alpha[atlasCol + 1, atlasRow] >= 10;
                bool hasTop = py < tp - 1 && alpha[atlasCol, atlasRow + 1] >= 10;
                bool hasBottom = py > 0 && alpha[atlasCol, atlasRow - 1] >= 10;

                AddPixelQuad(verts, new(x0, y0, z1), new(x1, y0, z1), new(x1, y1, z1), new(x0, y1, z1), u, v, 0, 0, 1);
                AddPixelQuad(verts, new(x1, y0, z0), new(x0, y0, z0), new(x0, y1, z0), new(x1, y1, z0), u, v, 0, 0, -1);

                if (!hasLeft)
                    AddPixelQuad(verts, new(x0, y0, z0), new(x0, y0, z1), new(x0, y1, z1), new(x0, y1, z0), u, v, -1, 0, 0);
                if (!hasRight)
                    AddPixelQuad(verts, new(x1, y0, z1), new(x1, y0, z0), new(x1, y1, z0), new(x1, y1, z1), u, v, 1, 0, 0);
                if (!hasBottom)
                    AddPixelQuad(verts, new(x0, y0, z1), new(x1, y0, z1), new(x1, y0, z0), new(x0, y0, z0), u, v, 0, -1, 0);
                if (!hasTop)
                    AddPixelQuad(verts, new(x0, y1, z0), new(x1, y1, z0), new(x1, y1, z1), new(x0, y1, z1), u, v, 0, 1, 0);
            }
        }

        return verts.ToArray();
    }

    // Every corner of a pixel's quad samples the same UV (the pixel's centre), not the true corner
    // UVs, or bilinear filtering would bleed neighbouring atlas pixels in at the edges.
    private static void AddPixelQuad(List<float> verts, Vector3 v0, Vector3 v1, Vector3 v2, Vector3 v3,
                                      float u, float v, float nx, float ny, float nz)
    {
        V(verts, v0.X, v0.Y, v0.Z, u, v, nx, ny, nz);
        V(verts, v1.X, v1.Y, v1.Z, u, v, nx, ny, nz);
        V(verts, v2.X, v2.Y, v2.Z, u, v, nx, ny, nz);
        V(verts, v0.X, v0.Y, v0.Z, u, v, nx, ny, nz);
        V(verts, v2.X, v2.Y, v2.Z, u, v, nx, ny, nz);
        V(verts, v3.X, v3.Y, v3.Z, u, v, nx, ny, nz);
    }

    // 12 triangles for an axis-aligned box, using each block's own per-face texture coordinates so a
    // dropped or held block matches the placed one. Winding and normals are hand-picked per face for
    // correct backface culling and lighting.
    private static void AddBox(List<float> verts, Block block, Vector3 min, Vector3 max)
    {
        var top = block.TopTextureCoords;
        var bot = block.BottomTextureCoords;
        var front = block.FrontTextureCoords;
        var back = block.BackTextureCoords;
        var right = block.RightTextureCoords;
        var left = block.LeftTextureCoords;

        float t_u0 = top.TopLeft.X, t_v0 = top.TopLeft.Y, t_u1 = top.BottomRight.X, t_v1 = top.BottomRight.Y;
        float b_u0 = bot.TopLeft.X, b_v0 = bot.TopLeft.Y, b_u1 = bot.BottomRight.X, b_v1 = bot.BottomRight.Y;
        float fr_u0 = front.TopLeft.X, fr_v0 = front.TopLeft.Y, fr_u1 = front.BottomRight.X, fr_v1 = front.BottomRight.Y;
        float bk_u0 = back.TopLeft.X, bk_v0 = back.TopLeft.Y, bk_u1 = back.BottomRight.X, bk_v1 = back.BottomRight.Y;
        float r_u0 = right.TopLeft.X, r_v0 = right.TopLeft.Y, r_u1 = right.BottomRight.X, r_v1 = right.BottomRight.Y;
        float l_u0 = left.TopLeft.X, l_v0 = left.TopLeft.Y, l_u1 = left.BottomRight.X, l_v1 = left.BottomRight.Y;

        float x0 = min.X, x1 = max.X;
        float y0 = min.Y, y1 = max.Y;
        float z0 = min.Z, z1 = max.Z;

        // Front (+Z)
        V(verts, x0, y0, z1, fr_u0, fr_v0, 0, 0, 1); V(verts, x1, y1, z1, fr_u1, fr_v1, 0, 0, 1); V(verts, x0, y1, z1, fr_u0, fr_v1, 0, 0, 1);
        V(verts, x0, y0, z1, fr_u0, fr_v0, 0, 0, 1); V(verts, x1, y0, z1, fr_u1, fr_v0, 0, 0, 1); V(verts, x1, y1, z1, fr_u1, fr_v1, 0, 0, 1);
        // Back (-Z)
        V(verts, x0, y0, z0, bk_u1, bk_v0, 0, 0, -1); V(verts, x0, y1, z0, bk_u1, bk_v1, 0, 0, -1); V(verts, x1, y1, z0, bk_u0, bk_v1, 0, 0, -1);
        V(verts, x0, y0, z0, bk_u1, bk_v0, 0, 0, -1); V(verts, x1, y1, z0, bk_u0, bk_v1, 0, 0, -1); V(verts, x1, y0, z0, bk_u0, bk_v0, 0, 0, -1);
        // Top (+Y)
        V(verts, x0, y1, z0, t_u0, t_v0, 0, 1, 0); V(verts, x1, y1, z1, t_u1, t_v1, 0, 1, 0); V(verts, x1, y1, z0, t_u1, t_v0, 0, 1, 0);
        V(verts, x0, y1, z0, t_u0, t_v0, 0, 1, 0); V(verts, x0, y1, z1, t_u0, t_v1, 0, 1, 0); V(verts, x1, y1, z1, t_u1, t_v1, 0, 1, 0);
        // Bottom (-Y)
        V(verts, x0, y0, z0, b_u0, b_v1, 0, -1, 0); V(verts, x1, y0, z0, b_u1, b_v1, 0, -1, 0); V(verts, x1, y0, z1, b_u1, b_v0, 0, -1, 0);
        V(verts, x0, y0, z0, b_u0, b_v1, 0, -1, 0); V(verts, x1, y0, z1, b_u1, b_v0, 0, -1, 0); V(verts, x0, y0, z1, b_u0, b_v0, 0, -1, 0);
        // Right (+X)
        V(verts, x1, y0, z0, r_u1, r_v0, 1, 0, 0); V(verts, x1, y1, z0, r_u1, r_v1, 1, 0, 0); V(verts, x1, y1, z1, r_u0, r_v1, 1, 0, 0);
        V(verts, x1, y0, z0, r_u1, r_v0, 1, 0, 0); V(verts, x1, y1, z1, r_u0, r_v1, 1, 0, 0); V(verts, x1, y0, z1, r_u0, r_v0, 1, 0, 0);
        // Left (-X)
        V(verts, x0, y0, z1, l_u1, l_v0, -1, 0, 0); V(verts, x0, y1, z1, l_u1, l_v1, -1, 0, 0); V(verts, x0, y1, z0, l_u0, l_v1, -1, 0, 0);
        V(verts, x0, y0, z1, l_u1, l_v0, -1, 0, 0); V(verts, x0, y1, z0, l_u0, l_v1, -1, 0, 0); V(verts, x0, y0, z0, l_u0, l_v0, -1, 0, 0);
    }

    private static void V(List<float> v, float px, float py, float pz, float u, float vv,
                          float nx, float ny, float nz)
    {
        v.Add(px); v.Add(py); v.Add(pz);
        v.Add(u); v.Add(vv);
        v.Add(nx); v.Add(ny); v.Add(nz);
    }
}
