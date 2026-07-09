using Silk.NET.OpenGL;

namespace VoxelEngine.Rendering;

/// <summary>
/// Off-screen render target used for the ASCII post-process effect: the normal scene is rendered into this framebuffer's color texture (with a depth/stencil renderbuffer for correct depth testing), and a later full-screen pass samples <see cref="ColorTexture"/> to convert pixel blocks into ASCII glyphs before drawing to the default framebuffer.
/// </summary>
public class AsciiFramebuffer : IDisposable
{
    /// <summary>GL handle of the framebuffer object that owns the color/depth attachments below.</summary>
    public uint Fbo { get; private set; }
    /// <summary>GL handle of the color attachment scene color is rendered into; sampled by the ASCII shader pass.</summary>
    public uint ColorTexture { get; private set; }
    // Combined depth+stencil renderbuffer attachment; not sampled directly, only used so depth testing behaves correctly while rendering the scene into this offscreen target.
    private uint _rbo;
    // Framebuffer dimensions in pixels, matching the window/render resolution this was created for.
    private int _width, _height;

    /// <summary>
    /// Allocates the framebuffer, its color texture, and a depth/stencil renderbuffer at the given pixel dimensions. Must be recreated (not resized) if the render resolution changes.
    /// </summary>
    public AsciiFramebuffer(int width, int height)
    {
        _width = width;
        _height = height;

        var gl = GlContext.Gl;

        Fbo = gl.GenFramebuffer();
        gl.BindFramebuffer(FramebufferTarget.Framebuffer, Fbo);

        ColorTexture = gl.GenTexture();
        gl.BindTexture(TextureTarget.Texture2D, ColorTexture);
        unsafe
        {
            // Allocate storage only (null data pointer) - the texture is populated later by rendering into the framebuffer, not by uploading pixel data here.
            gl.TexImage2D(TextureTarget.Texture2D, 0, InternalFormat.Rgb,
                (uint)width, (uint)height, 0, PixelFormat.Rgb, PixelType.UnsignedByte, (void*)null);
        }
        // Nearest filtering keeps the source pixels crisp/blocky, which matters since the ASCII pass samples fixed-size pixel blocks to choose a glyph - smoothing would blur that signal.
        gl.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMinFilter, (int)TextureMinFilter.Nearest);
        gl.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMagFilter, (int)TextureMagFilter.Nearest);
        gl.FramebufferTexture2D(FramebufferTarget.Framebuffer, FramebufferAttachment.ColorAttachment0,
            TextureTarget.Texture2D, ColorTexture, 0);

        // Depth24Stencil8 renderbuffer: needed so normal depth-tested scene rendering works when targeting this FBO, even though only color is read back afterward.
        _rbo = gl.GenRenderbuffer();
        gl.BindRenderbuffer(RenderbufferTarget.Renderbuffer, _rbo);
        gl.RenderbufferStorage(RenderbufferTarget.Renderbuffer, InternalFormat.Depth24Stencil8, (uint)width, (uint)height);
        gl.FramebufferRenderbuffer(FramebufferTarget.Framebuffer, FramebufferAttachment.DepthStencilAttachment,
            RenderbufferTarget.Renderbuffer, _rbo);

        // Unbind so subsequent GL calls elsewhere don't accidentally render into this FBO.
        gl.BindFramebuffer(FramebufferTarget.Framebuffer, 0);
    }

    /// <summary>Releases the framebuffer, color texture, and renderbuffer GL objects.</summary>
    public void Dispose()
    {
        var gl = GlContext.Gl;
        gl.DeleteFramebuffer(Fbo);
        gl.DeleteTexture(ColorTexture);
        gl.DeleteRenderbuffer(_rbo);
    }
}
