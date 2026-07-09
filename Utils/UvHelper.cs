// A helper class that is used for loading block textures from the texture atlas. | DA | 2/5/26

using VoxelEngine.Rendering;

namespace VoxelEngine.Utils
{
    /// <summary>
    /// Converts positions on a fixed-grid texture atlas (world.png / Items.png) into normalized [0,1] UV coordinates usable by GL. The atlas is a square grid of TILE_COUNT x TILE_COUNT tiles (16x16 by default, matching classic Minecraft-style texture packs), where each tile is one block/item texture. Tile (0,0) is the top-left of the image; since image origin (row 0) corresponds to the top of the atlas and OpenGL texture V=0 is conventionally the bottom, callers/shaders are expected to handle any needed V-flip - this helper just maps tile indices to UV rectangles consistently with how the atlas image is laid out.
    /// </summary>
    public class UvHelper
    {
        // Number of tiles along each edge of the square atlas texture.
        public const int TILE_COUNT = 16;
        // Size (in normalized UV space) of a single tile: 1 / TILE_COUNT.
        private const float TILE_SIZE = 1.0f / TILE_COUNT;

        /// <summary>
        /// Returns the UV rectangle (top-left/bottom-right corners, in normalized 0..1 atlas space) for the tile at grid column x, row y. This is the standard lookup used for full-tile block/item textures.
        /// </summary>
        public static TextureCoords FromTileCoords(int x, int y)
        {
            // Each tile occupies [x*TILE_SIZE, (x+1)*TILE_SIZE) horizontally and the same vertically for y - simple grid-cell-to-UV-rect mapping.
            Vector2 topLeft = new Vector2(x * TILE_SIZE, y * TILE_SIZE);
            Vector2 bottomRight = new Vector2((x + 1) * TILE_SIZE, (y + 1) * TILE_SIZE);

            return new TextureCoords
            {
                TopLeft = topLeft,
                BottomRight = bottomRight
            };
        }

        /// <summary>
        /// Returns the UV rectangle for a sub-region within a single tile, specified in pixel coordinates assuming each tile is a 16x16 pixel texture (the classic block texture resolution). Used for effects like partial/cropped texture sampling (e.g. animated or masked textures) rather than a whole tile.
        /// </summary>
        public static TextureCoords FromPartialTile(int tileX, int tileY, float startPixelX, float startPixelY,
            float widthPixels, float heightPixels)
        {
            // Size, in normalized UV space, of a single pixel within a tile (tile is assumed to be a 16x16-pixel source texture).
            float tilePixelSize = TILE_SIZE / 16f;

            // UV-space origin of the containing tile.
            float baseX = tileX * TILE_SIZE;
            float baseY = tileY * TILE_SIZE;

            // Offset from the tile origin by the requested pixel rectangle, converting pixel units to UV units via tilePixelSize.
            Vector2 topLeft = new Vector2(
                baseX + startPixelX * tilePixelSize,
                baseY + startPixelY * tilePixelSize
            );

            Vector2 bottomRight = new Vector2(
                baseX + (startPixelX + widthPixels) * tilePixelSize,
                baseY + (startPixelY + heightPixels) * tilePixelSize
            );

            return new TextureCoords
            {
                TopLeft = topLeft,
                BottomRight = bottomRight
            };
        }

        /// <summary>
        /// Picks a random square sub-region of size pixelSize x pixelSize (in the tile's 16x16 pixel space) within the given tile's UV rectangle. Used to give particles (e.g. block-break debris) visual variety by sampling a random small patch of the block's texture instead of the whole tile.
        /// </summary>
        public static TextureCoords GetRandomSubTile(TextureCoords tileCoords, Random random, int pixelSize = 4)
        {
            // Clamp the random start position so the pixelSize x pixelSize patch never runs past the edge of the 16x16 tile.
            int maxStart = 16 - pixelSize;
            int startPixelX = random.Next(0, maxStart + 1);
            int startPixelY = random.Next(0, maxStart + 1);

            float tilePixelSize = TILE_SIZE / 16f;

            // tileCoords.TopLeft is the UV origin of the whole tile; offset into it by the randomly chosen pixel position to get the sub-tile's UV origin.
            Vector2 topLeft = tileCoords.TopLeft + new Vector2(
                startPixelX * tilePixelSize,
                startPixelY * tilePixelSize
            );

            Vector2 bottomRight = topLeft + new Vector2(
                pixelSize * tilePixelSize,
                pixelSize * tilePixelSize
            );

            return new TextureCoords
            {
                TopLeft = topLeft,
                BottomRight = bottomRight
            };
        }
    }
}