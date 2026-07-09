// Face-building methods extracted from Chunk.cs into a partial class. These methods decide which faces to emit for each block type, then delegate vertex emission to ChunkMeshBuilder.
using System.Collections.Generic;
using VoxelEngine.Rendering;
using VoxelEngine.Terrain.Blocks;
using VoxelEngine.Utils;

namespace VoxelEngine.Terrain;

public partial class Chunk
{
    // Standard full-cube block: for each of the 6 directions, only emit that face if the neighboring block is transparent (i.e. the face would actually be visible) - this is the core of greedy face culling that keeps chunk meshes from including hidden interior faces.
    private void BuildBlockFaces(List<float> verts, int x, int y, int z, float wx, float wz, BlockType block)
    {
        foreach (var (face, dx, dy, dz) in FaceDirections)
        {
            int nx = x + dx, ny = y + dy, nz = z + dz;

            if (IsTransparent(nx, ny, nz))
                ChunkMeshBuilder.AddFace(verts, wx, y, wz, face, block, GetSkyLightAt(nx, ny, nz), GetBlockLightAt(nx, ny, nz));
        }
    }

    // Farmland's top texture varies by tilled/hydrated state stored in metadata (dry vs wet soil), unlike other faces which use the block's fixed textures.
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

    // Blocks whose front texture rotates with placement direction (furnaces, pumpkins, etc.): top/bottom textures are fixed, but the 4 horizontal faces are resolved per-face based on the stored facing metadata via GetFacingTexture.
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

    // Metadata encoding: 0=North(-Z), 1=South(+Z), 2=East(+X), 3=West(-X). The block faces the player, so its front is opposite to the camera's look direction.
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

    // Double chests span 2 adjacent blocks but must render as one seamless chest texture; the "canonical" half (see IsDoubleChestCanonical) picks one half of a split front/back texture and the other half picks the complementary half, so the two blocks visually align.
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

    // Deterministically designates one half of each double-chest pair as "canonical" (the one using the first texture half) by picking the half with the higher +X/+Z neighbor coordinate, so both blocks agree on which is which without needing extra stored state.
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

    // Slabs occupy only the bottom half of the block cell (0 to 0.5 in Y); the top face is drawn at half-height and other faces are half-height quads, still culled against neighbors.
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

    // Stairs are built from 2 boxes: a full-footprint bottom slab plus a half-footprint top "back step" whose position depends on facing, giving the classic L-shaped stair silhouette.
    private void BuildStairFaces(List<float> verts, int x, int y, int z, float wx, float wz, BlockType block, int facing)
    {
        int skyLight = GetSkyLightAt(x, y, z);
        int blockLight = GetBlockLightAt(x, y, z);

        // Box 1: bottom slab (full X/Z, bottom half)
        ChunkMeshBuilder.AddStairBox(verts, wx, y, wz, wx + 1, y + 0.5f, wz + 1, block, skyLight, blockLight);

        // Box 2: back step (half extent on one axis based on facing, top half) Facing: 0=North(-Z), 1=South(+Z), 2=East(+X), 3=West(-X)
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

    // Water/lava surfaces sit slightly below the full block height (WATER_SURFACE_HEIGHT) unless there's another fluid block directly above, in which case the top is flush (y+1) so stacked fluid columns don't show a gap between blocks.
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

    private bool ShouldDrawWaterFace(int x, int y, int z)
    {
        var neighbor = GetBlockAt(x, y, z);
        return neighbor != BlockType.Water && BlockRegistry.IsTransparent(neighbor);
    }
}
