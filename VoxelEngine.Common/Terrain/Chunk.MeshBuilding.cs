// Face-building methods extracted from Chunk.cs into a partial class. These methods decide
// which faces to emit for each block type, then delegate vertex emission to ChunkMeshBuilder.
using System.Collections.Generic;
using VoxelEngine.Rendering;
using VoxelEngine.Terrain.Blocks;
using VoxelEngine.Utils;

namespace VoxelEngine.Terrain;

public partial class Chunk
{
    // Standard full-cube block: for each of the 6 directions, only emit that face if the
    // neighboring block is transparent (i.e. the face would actually be visible) - this is the
    // core of greedy face culling that keeps chunk meshes from including hidden interior faces.
    private void BuildBlockFaces(List<float> verts, int x, int y, int z, float wx, float wz, BlockType block)
    {
        foreach (var (face, dx, dy, dz) in FaceDirections)
        {
            int nx = x + dx, ny = y + dy, nz = z + dz;

            if (IsTransparent(nx, ny, nz))
                ChunkMeshBuilder.AddFace(verts, wx, y, wz, face, block, GetSkyLightAt(nx, ny, nz), GetBlockLightAt(nx, ny, nz));
        }
    }

    // Farmland's top texture varies by tilled/hydrated state stored in metadata (dry vs wet soil),
    // unlike other faces which use the block's fixed textures.
    private void BuildFarmlandFaces(List<float> verts, int x, int y, int z, float wx, float wz)
    {
        byte meta = (byte)GetMetadata(x, y, z);
        var topTex = BlockRegistry.Get(BlockType.Farmland).GetTopTexture(meta);
        var bottomTex = BlockRegistry.GetBottomTexture(BlockType.Farmland);
        var sideTex = BlockRegistry.GetSideTexture(BlockType.Farmland);

        foreach (var (face, dx, dy, dz) in FaceDirections)
        {
            int nx = x + dx, ny = y + dy, nz = z + dz;
            if (!IsTransparent(nx, ny, nz)) continue;

            var tex = face switch
            {
                Face.Top => topTex,
                Face.Bottom => bottomTex,
                _ => sideTex
            };
            ChunkMeshBuilder.AddFace(verts, wx, y, wz, face, tex, GetSkyLightAt(nx, ny, nz), GetBlockLightAt(nx, ny, nz));
        }
    }

    // Blocks whose front texture rotates with placement direction (furnaces, pumpkins, etc.):
    // top/bottom textures are fixed, but the 4 horizontal faces are resolved per-face based on the
    // stored facing metadata via GetFacingTexture.
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
                _ => GetFacingTexture(face, facing, frontTex, sideTex, sideTex)
            };

            ChunkMeshBuilder.AddFace(verts, wx, y, wz, face, tex, GetSkyLightAt(nx, ny, nz), GetBlockLightAt(nx, ny, nz));
        }
    }

    // Metadata encoding: 0=North(-Z), 1=South(+Z), 2=East(+X), 3=West(-X).
    // The block faces the player, so its front is opposite to the camera's look direction.
    private TextureCoords GetFacingTexture(Face geometricFace, int facing, TextureCoords frontTex, TextureCoords backTex, TextureCoords sideTex)
    {
        (Face frontFace, Face backFace) = facing switch
        {
            0 => (Face.Front, Face.Back),
            1 => (Face.Back, Face.Front),
            2 => (Face.Left, Face.Right),
            3 => (Face.Right, Face.Left),
            _ => (Face.Front, Face.Back)
        };

        if (geometricFace == frontFace) return frontTex;
        if (geometricFace == backFace) return backTex;
        return sideTex;
    }

    // Double chests span 2 adjacent blocks but must render as one seamless chest texture; the
    // "canonical" half (see IsDoubleChestCanonical) picks one half of a split front/back texture
    // and the other half picks the complementary half, so the two blocks visually align.
    private void BuildDoubleChestFaces(List<float> verts, int x, int y, int z, float wx, float wz)
    {
        int facing = GetMetadata(x, y, z);
        var topTex    = BlockRegistry.GetTopTexture(BlockType.DoubleChest);
        var bottomTex = BlockRegistry.GetBottomTexture(BlockType.DoubleChest);
        var sideTex   = BlockRegistry.GetSideTexture(BlockType.DoubleChest);

        bool isCanonical = IsDoubleChestCanonical(x, y, z);
        var frontTex = isCanonical ? UvHelper.FromTileCoords(0, 8) : UvHelper.FromTileCoords(1, 8);
        var backTex  = isCanonical ? UvHelper.FromTileCoords(1, 9) : UvHelper.FromTileCoords(0, 9);

        foreach (var (face, dx, dy, dz) in FaceDirections)
        {
            int nx = x + dx, ny = y + dy, nz = z + dz;
            if (!IsTransparent(nx, ny, nz))
                continue;

            var tex = face switch
            {
                Face.Top    => topTex,
                Face.Bottom => bottomTex,
                _           => GetFacingTexture(face, facing, frontTex, backTex, sideTex)
            };

            ChunkMeshBuilder.AddFace(verts, wx, y, wz, face, tex, GetSkyLightAt(nx, ny, nz), GetBlockLightAt(nx, ny, nz));
        }
    }

    // Deterministically designates one half of each double-chest pair as "canonical" (the one
    // using the first texture half) by picking the half with the higher +X/+Z neighbor coordinate,
    // so both blocks agree on which is which without needing extra stored state.
    private bool IsDoubleChestCanonical(int x, int y, int z)
    {
        (int dx, int dz)[] neighbors = [(1, 0), (-1, 0), (0, 1), (0, -1)];
        foreach (var (dx, dz) in neighbors)
        {
            if (GetBlockAt(x + dx, y, z + dz) == BlockType.DoubleChest)
                return dx > 0 || dz > 0;
        }
        return true;
    }

    // Slabs occupy only the bottom half of the block cell (0 to 0.5 in Y); the top face is drawn
    // at half-height and other faces are half-height quads, still culled against neighbors.
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

    // Stairs are built from 2 boxes: a full-footprint bottom slab plus a half-footprint top "back
    // step" whose position depends on facing, giving the classic L-shaped stair silhouette.
    private void BuildStairFaces(List<float> verts, int x, int y, int z, float wx, float wz, BlockType block, int facing)
    {
        int skyLight = GetSkyLightAt(x, y, z);
        int blockLight = GetBlockLightAt(x, y, z);

        // Box 1: bottom slab (full X/Z, bottom half)
        ChunkMeshBuilder.AddStairBox(verts, wx, y, wz, wx + 1, y + 0.5f, wz + 1, block, skyLight, blockLight);

        // Box 2: back step (half extent on one axis based on facing, top half)
        // Facing: 0=North(-Z), 1=South(+Z), 2=East(+X), 3=West(-X)
        switch (facing)
        {
            case 0:
                ChunkMeshBuilder.AddStairBox(verts, wx, y + 0.5f, wz, wx + 1, y + 1, wz + 0.5f, block, skyLight, blockLight);
                break;
            case 1:
                ChunkMeshBuilder.AddStairBox(verts, wx, y + 0.5f, wz + 0.5f, wx + 1, y + 1, wz + 1, block, skyLight, blockLight);
                break;
            case 2:
                ChunkMeshBuilder.AddStairBox(verts, wx + 0.5f, y + 0.5f, wz, wx + 1, y + 1, wz + 1, block, skyLight, blockLight);
                break;
            case 3:
                ChunkMeshBuilder.AddStairBox(verts, wx, y + 0.5f, wz, wx + 0.5f, y + 1, wz + 1, block, skyLight, blockLight);
                break;
        }
    }

    private void BuildWaterFaces(List<float> verts, int x, int y, int z, float wx, float wz, BlockType block)
        => BuildFluidFaces(verts, x, y, z, wx, wz, block);

    private void BuildLavaFaces(List<float> verts, int x, int y, int z, float wx, float wz, BlockType block)
        => BuildFluidFaces(verts, x, y, z, wx, wz, block);

    /// <summary>
    /// Emits a fluid cell. The surface height now comes from the cell's level metadata rather than
    /// a fixed constant, and each of the four top corners is averaged against the neighbouring
    /// cells that touch it - so a stream running downhill renders as one continuous sloping sheet
    /// instead of a staircase, and the edge of a pool tapers off.
    /// A cell with the same fluid directly above is flush to the top of the block, which keeps a
    /// deep pool or a waterfall from showing seams between its layers.
    /// </summary>
    private void BuildFluidFaces(List<float> verts, int x, int y, int z, float wx, float wz, BlockType block)
    {
        bool fluidAbove = GetBlockAt(x, y + 1, z) == block;

        // Only moving fluid animates. Metadata 0 is a source - a lake or an ocean - and a still
        // body of water should look still; scrolling its texture makes the whole world feel like
        // it's drifting. Anything with a level, or fed from above, is a stream or a fall and does.
        bool animate = GetMetadata(x, y, z) != 0;

        float h00, h10, h11, h01;
        if (fluidAbove)
        {
            h00 = h10 = h11 = h01 = y + 1f;
        }
        else
        {
            h00 = FluidCornerHeight(x, y, z, block, 0, 0);
            h10 = FluidCornerHeight(x, y, z, block, 1, 0);
            h11 = FluidCornerHeight(x, y, z, block, 1, 1);
            h01 = FluidCornerHeight(x, y, z, block, 0, 1);
        }

        foreach (var (face, dx, dy, dz) in FaceDirections)
        {
            if (face == Face.Top)
            {
                if (!fluidAbove)
                    ChunkMeshBuilder.AddFluidFace(verts, wx, y, wz, face, block,
                        GetSkyLightAt(x, y + 1, z), GetBlockLightAt(x, y + 1, z), h00, h10, h11, h01,
                        animate);
            }
            else if (ShouldDrawFluidFace(x + dx, y + dy, z + dz, block))
            {
                ChunkMeshBuilder.AddFluidFace(verts, wx, y, wz, face, block,
                    GetSkyLightAt(x + dx, y + dy, z + dz), GetBlockLightAt(x + dx, y + dy, z + dz),
                    h00, h10, h11, h01, animate);
            }
        }
    }

    /// <summary>
    /// Height of one top corner, as vanilla computes it: average the fill of the up-to-four cells
    /// meeting at that corner. Sources and falling cells are weighted ten to one, so a pool stays
    /// flat and level right up to its edge and only the thin flowing fringe slopes away.
    /// </summary>
    private float FluidCornerHeight(int x, int y, int z, BlockType fluid, int cornerX, int cornerZ)
    {
        float total = 0f;
        int weight = 0;

        for (int i = 0; i < 4; i++)
        {
            int cx = x + cornerX - (i & 1);
            int cz = z + cornerZ - ((i >> 1) & 1);

            // Fluid stacked above this corner means it is full to the brim, no averaging needed.
            if (GetBlockAt(cx, y + 1, cz) == fluid)
                return y + 1f;

            var here = GetBlockAt(cx, y, cz);
            if (here != fluid)
            {
                // Open air pulls the corner down; a solid neighbour is skipped entirely so the
                // fluid stays level where it meets a wall rather than sagging into it.
                if (!BlockRegistry.IsSolid(here))
                {
                    total += 1f;
                    weight++;
                }

                continue;
            }

            int meta = GetMetadataAt(cx, y, cz);
            float fill = BlockFluid.FillFraction(meta);

            if (meta == 0 || (meta & 8) != 0)
            {
                total += fill * 10f;
                weight += 10;
            }

            total += fill;
            weight++;
        }

        if (weight == 0)
            return y + WATER_SURFACE_HEIGHT;

        return y + 1f - total / weight;
    }

    private bool ShouldDrawFluidFace(int x, int y, int z, BlockType fluid)
    {
        var neighbor = GetBlockAt(x, y, z);
        return neighbor != fluid && BlockRegistry.IsTransparent(neighbor);
    }


}
