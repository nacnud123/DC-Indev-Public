// A helper class that is used for loading block textures from the texture atlas. | DA | 2/5/26
using OpenTK.Mathematics;
using VoxelEngine.Rendering;

namespace VoxelEngine.Utils
{
    public class UvHelper
    {
        private const int TILE_COUNT = 8;
        private const float TILE_SIZE = 1.0f / TILE_COUNT;

        public static TextureCoords FromTileCoords(int x, int y)
        {
            Vector2 topLeft = new Vector2(x * TILE_SIZE, y * TILE_SIZE);
            Vector2 bottomRight = new Vector2((x + 1) * TILE_SIZE, (y + 1) * TILE_SIZE);

            return new TextureCoords
            {
                TopLeft = topLeft,
                BottomRight = bottomRight
            };
        }

        public static TextureCoords FromPartialTile(int tileX, int tileY, float startPixelX, float startPixelY,
            float widthPixels, float heightPixels)
        {
            float tilePixelSize = TILE_SIZE / 16f;

            float baseX = tileX * TILE_SIZE;
            float baseY = tileY * TILE_SIZE;

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

        public static TextureCoords GetRandomSubTile(TextureCoords tileCoords, Random random, int pixelSize = 4)
        {
            int maxStart = 16 - pixelSize;
            int startPixelX = random.Next(0, maxStart + 1);
            int startPixelY = random.Next(0, maxStart + 1);

            float tilePixelSize = TILE_SIZE / 16f;

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