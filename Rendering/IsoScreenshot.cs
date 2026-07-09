// Renders the whole world as a cave-free isometric PNG and saves it to Documents/mc_map_NNNN.png | DA | 3/2/26 CPU-side top-down scanline: for each (X,Z) column the topmost non-air block is found, its top-face atlas tile is averaged to a single colour, height-based shading is applied, and the result is painted into a 2:1 dimetric canvas (2 px wide x 1 px tall per block). No GL rendering - caves can never show.
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Processing;
using VoxelEngine.Terrain;
using VoxelEngine.Terrain.Blocks;

namespace VoxelEngine.Rendering;

/// <summary>
/// Produces a static top-down "map" style isometric snapshot of the world and writes it to disk as a PNG. Bound to the F7 key in-game. Important: this class does NOT touch OpenGL or the GPU at all - it is a pure CPU rasterizer that walks the <see cref="World"/> block data directly and paints pixels into a byte buffer with ImageSharp. Because it only ever looks at the topmost solid block per column, caves, overhangs, and anything underground can never appear in the output.
/// </summary>
public class IsoScreenshot
{
    private readonly World mWorld;
    private readonly float mTimeOfDay; // used to compute sun angle / overall brightness of the snapshot

    // The block texture atlas (world.png) is a fixed 16x16 grid of 16x16 pixel tiles -> 256x256 total.
    private const int ATLAS_TILES  = 16;
    private const int TILE_PIXELS  = 16;
    private const int ATLAS_PIXELS = ATLAS_TILES * TILE_PIXELS; // 256

    // CPU-side copy of world.png's RGBA pixels, loaded once per Capture() call so SampleTopColor can average tile colors without touching the GPU texture.
    private byte[]? mAtlasRgba;

    public IsoScreenshot(World world, float timeOfDay)
    {
        mWorld     = world;
        mTimeOfDay = timeOfDay;
    }

    // Renders the world and saves the PNG. Call from the game thread.
    public void Capture()
    {
        mAtlasRgba = LoadAtlas("Resources/world.png");

        int worldW = mWorld.SizeInChunks * Chunk.WIDTH;
        int worldL = mWorld.SizeInChunks * Chunk.DEPTH;
        int worldH = Chunk.HEIGHT;

        // 2:1 dimetric ("Minecraft map style") projection: each world column contributes a 2px-wide, 1px-tall diamond footprint, and each unit of height adds 2px of vertical offset. The canvas must be wide/tall enough to hold every diagonal (X-Z) and vertical (Y) projection without clipping.
        int canvasW = (worldW + worldL) * 2;
        int canvasH = (worldW + worldL) + worldH * 2;

        var canvas = new byte[canvasW * canvasH * 4];

        // Reuses the same day/night brightness curve as the live sky: sin() of the time-of-day angle, clamped so it never fully goes to black (0.05 floor) and normalized back to a 0..1 dayFactor.
        float sunAngle      = mTimeOfDay * MathF.PI * 2f;
        float sunlightLevel = Math.Clamp(MathF.Sin(sunAngle) * 2f, 0.05f, 1.0f);
        float dayFactor     = (sunlightLevel - 0.05f) / 0.95f;

        // Scan every world column (X,Z) independently - this is what makes the render "cave-free": only the highest surface block per column is ever sampled, so anything beneath it is invisible.
        for (int worldX = 0; worldX < worldW; worldX++)
        {
            for (int worldZ = 0; worldZ < worldL; worldZ++)
            {
                int topY = FindTopBlock(worldX, worldZ, worldH);
                if (topY < 0)
                    continue;

                var block = mWorld.GetBlock(worldX, topY, worldZ);
                (byte r, byte g, byte b) = SampleTopColor(block);

                // Higher terrain is brighter; lower terrain is darker
                float heightFrac  = topY / (float)(worldH - 1);
                float shade       = Math.Clamp(0.35f + dayFactor * 0.35f + heightFrac * 0.30f, 0f, 1f);

                byte fr = (byte)Math.Clamp(r * shade, 0, 255);
                byte fg = (byte)Math.Clamp(g * shade, 0, 255);
                byte fb = (byte)Math.Clamp(b * shade, 0, 255);

                // Dimetric projection: X-Z diagonal position maps to canvas X (world X minus Z, scaled and re-centered by canvasW/2), while canvas Y combines the X+Z diagonal depth with a height offset so taller columns are drawn further up the canvas.
                int cx = (worldX - worldZ) * 2 + canvasW / 2;
                int cy = (worldX + worldZ)      + (worldH - 1 - topY) * 2;

                // Each column paints a 2px-wide, 1px-tall strip (the "top" of the diamond tile).
                PaintPixel(canvas, canvasW, canvasH, cx,     cy, fr, fg, fb);
                PaintPixel(canvas, canvasW, canvasH, cx + 1, cy, fr, fg, fb);
            }
        }

        string path = FindOutputPath();
        SavePng(canvas, canvasW, canvasH, path);
        Console.WriteLine($"[IsoScreenshot] Saved to {path}");
    }

    /// <summary>
    /// Scans downward and returns the Y of the first non-air block, or -1 for an empty column. This top-down-only scan is the entire reason caves never show up in the screenshot.
    /// </summary>
    private int FindTopBlock(int worldX, int worldZ, int worldH)
    {
        for (int y = worldH - 1; y >= 0; y--)
        {
            if (mWorld.GetBlock(worldX, y, worldZ) != BlockType.Air)
                return y;
        }
        return -1;
    }

    /// <summary>
    /// Returns the average RGB of a block's top-face atlas tile. UvHelper tile rows use OpenGL bottom-left origin, so the row is flipped before sampling.
    /// </summary>
    private (byte r, byte g, byte b) SampleTopColor(BlockType blockType)
    {
        if (mAtlasRgba == null)
            return (128, 128, 128);

        // TopLeft.X/Y are normalized (0..1) UV coordinates; multiplying by ATLAS_TILES converts them back into integer tile-grid column/row indices (see TextureCoords.cs / UvHelper).
        var coords  = BlockRegistry.GetTopTexture(blockType);
        int tileCol = Math.Clamp((int)(coords.TopLeft.X * ATLAS_TILES), 0, ATLAS_TILES - 1);
        int tileRow = Math.Clamp((int)(coords.TopLeft.Y * ATLAS_TILES), 0, ATLAS_TILES - 1);

        // UvHelper row 0 = bottom of PNG (OpenGL origin); ImageSharp row 0 = top - flip it
        int startX = tileCol * TILE_PIXELS;
        int startY = (ATLAS_TILES - 1 - tileRow) * TILE_PIXELS;

        long sumR = 0, sumG = 0, sumB = 0, count = 0;
        for (int py = startY; py < startY + TILE_PIXELS; py++)
        {
            for (int px = startX; px < startX + TILE_PIXELS; px++)
            {
                int idx = (py * ATLAS_PIXELS + px) * 4;
                if (mAtlasRgba[idx + 3] == 0)
                    continue; // skip fully transparent pixels
                sumR += mAtlasRgba[idx + 0];
                sumG += mAtlasRgba[idx + 1];
                sumB += mAtlasRgba[idx + 2];
                count++;
            }
        }

        if (count == 0)
            return (128, 128, 128);

        return ((byte)(sumR / count), (byte)(sumG / count), (byte)(sumB / count));
    }

    // Writes a single opaque RGBA pixel into the flat canvas buffer, bounds-checked via unsigned comparison (a negative cx/cy wraps to a huge uint and fails the >= check, same as an overflow).
    private static void PaintPixel(byte[] canvas, int canvasW, int canvasH, int cx, int cy, byte r, byte g, byte b)
    {
        if ((uint)cx >= (uint)canvasW || (uint)cy >= (uint)canvasH)
            return;

        int idx = (cy * canvasW + cx) * 4;
        canvas[idx + 0] = r;
        canvas[idx + 1] = g;
        canvas[idx + 2] = b;
        canvas[idx + 3] = 255;
    }

    // Loads world.png into a CPU-side RGBA buffer without flipping
    private static byte[] LoadAtlas(string path)
    {
        using var image = Image.Load<Rgba32>(path);

        if (image.Width != ATLAS_PIXELS || image.Height != ATLAS_PIXELS)
            image.Mutate(ctx => ctx.Resize(ATLAS_PIXELS, ATLAS_PIXELS));

        var buffer = new byte[ATLAS_PIXELS * ATLAS_PIXELS * 4];
        image.CopyPixelDataTo(buffer);
        return buffer;
    }

    // Returns the next unused Documents/mc_map_NNNN.png path
    private static string FindOutputPath()
    {
        string dir = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
        for (int n = 0; n < 10000; n++)
        {
            string path = Path.Combine(dir, $"mc_map_{n:D4}.png");
            if (!File.Exists(path))
                return path;
        }
        return Path.Combine(dir, "mc_map_9999.png");
    }

    private static void SavePng(byte[] rgba, int width, int height, string path)
    {
        using var image = Image.LoadPixelData<Rgba32>(rgba, width, height);
        image.SaveAsPng(path);
    }
}
