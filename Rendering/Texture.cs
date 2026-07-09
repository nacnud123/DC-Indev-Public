// Main texture class, does GL stuff to make texture | DA | 2/5/26
using Silk.NET.OpenGL;
using StbImageSharp;

/// <summary>
/// Wraps a single GL 2D texture object. Handles loading image files from disk via StbImageSharp, uploading them to the GPU with the pixel-art-appropriate filtering/wrap settings this game uses everywhere (nearest filtering, no mipmaps by default), and cleaning up the GL object on disposal. Used for the world/item texture atlases as well as one-off textures like the sun/moon and paintings.
/// </summary>
public class Texture : IDisposable
{
    public readonly uint Handle;
    public int Width { get; private set; }
    public int Height { get; private set; }

    private bool mDisposed = false;

    // Wraps an already-created GL texture handle (e.g. one built by a render-to-texture path) rather than loading from disk. Width/Height are optional metadata, not required for the texture to function.
    public Texture(uint glHandle, int width = 0, int height = 0)
    {
        Handle = glHandle;
        Width = width;
        Height = height;
    }

    /// <summary>
    /// Loads an image file from disk, uploads it as a GL texture, and configures sampling parameters. Defaults (Nearest filtering, ClampToEdge wrap, no mipmaps) match this game's pixel-art atlas style: nearest filtering avoids blurring blocky textures, and atlases are typically not tiled so ClampToEdge prevents bleeding between adjacent atlas tiles at their edges.
    /// </summary>
    public static Texture LoadFromFile(string path, bool repeat = false, bool mipmaps = false)
    {
        var gl = VoxelEngine.Rendering.GlContext.Gl;
        uint handle = gl.GenTexture();

        gl.ActiveTexture(TextureUnit.Texture0);
        gl.BindTexture(TextureTarget.Texture2D, handle);

        // Image files are typically stored top-left-origin, but OpenGL's texture (0,0) is bottom-left. Flipping on load means UV (0,0) in shaders correctly maps to the visual bottom-left of the image.
        StbImage.stbi_set_flip_vertically_on_load(1);

        int width, height;
        using (Stream stream = File.OpenRead(path))
        {
            ImageResult image = ImageResult.FromStream(stream, ColorComponents.RedGreenBlueAlpha);
            width = image.Width;
            height = image.Height;

            gl.TexImage2D<byte>(TextureTarget.Texture2D, 0, InternalFormat.Rgba,
                (uint)image.Width, (uint)image.Height, 0, PixelFormat.Rgba, PixelType.UnsignedByte, image.Data);
        }

        // Nearest (not Linear) filtering keeps the game's pixel-art textures crisp instead of blurring them when magnified/minified - critical for a blocky voxel aesthetic.
        gl.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMinFilter, (int)TextureMinFilter.Nearest);
        gl.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMagFilter, (int)TextureMagFilter.Nearest);

        gl.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureWrapS,
            repeat ? (int)TextureWrapMode.Repeat : (int)TextureWrapMode.ClampToEdge);
        gl.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureWrapT,
            repeat ? (int)TextureWrapMode.Repeat : (int)TextureWrapMode.ClampToEdge);

        // Transparent black border color; only relevant if a sampler ever uses ClampToBorder, but set defensively here for consistency across all textures loaded by this method.
        float[] borderColor = [0f, 0f, 0f, 0f];
        gl.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureBorderColor, borderColor);

        if (mipmaps)
        {
            gl.GenerateMipmap(TextureTarget.Texture2D);
        }
        else
        {
            // Explicitly clamp to a single mip level (0) when mipmaps aren't generated, so the driver doesn't treat the texture as incomplete (which would otherwise sample as black).
            gl.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMaxLevel, 0);
        }

        return new Texture(handle, width, height);
    }

    /// <summary>Binds this texture to the given texture unit for use by the next draw call(s).</summary>
    public void Use(TextureUnit unit)
    {
        var gl = VoxelEngine.Rendering.GlContext.Gl;
        gl.ActiveTexture(unit);
        gl.BindTexture(TextureTarget.Texture2D, Handle);
    }

    public void Dispose()
    {
        Dispose(true);
        GC.SuppressFinalize(this);
    }

    // Standard disposable pattern guard: prevents double-deleting the GL texture object if Dispose is called more than once. The try/catch around DeleteTexture swallows errors from deleting a texture whose GL context may already be gone (e.g. during shutdown), rather than throwing during cleanup.
    protected virtual void Dispose(bool disposing)
    {
        if (!mDisposed)
        {
            if (disposing)
            {
                try
                {
                    VoxelEngine.Rendering.GlContext.Gl.DeleteTexture(Handle);
                }
                catch
                {
                }
            }
            mDisposed = true;
        }
    }
}
