using OpenTK.Graphics.OpenGL4;

namespace VoxelEngine.Rendering;

public class AsciiFramebuffer : IDisposable
{
    public int Fbo { get; private set; }
    public int ColorTexture { get; private set; }
    private int _rbo;
    private int _width, _height;

    public AsciiFramebuffer(int width, int height)
    {
        _width = width;
        _height = height;

        Fbo = GL.GenFramebuffer();
        GL.BindFramebuffer(FramebufferTarget.Framebuffer, Fbo);

        ColorTexture = GL.GenTexture();
        GL.BindTexture(TextureTarget.Texture2D, ColorTexture);
        GL.TexImage2D(TextureTarget.Texture2D, 0, PixelInternalFormat.Rgb,
            width, height, 0, PixelFormat.Rgb, PixelType.UnsignedByte, IntPtr.Zero);
        GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMinFilter, (int)TextureMinFilter.Nearest);
        GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMagFilter, (int)TextureMagFilter.Nearest);
        GL.FramebufferTexture2D(FramebufferTarget.Framebuffer, FramebufferAttachment.ColorAttachment0,
            TextureTarget.Texture2D, ColorTexture, 0);

        _rbo = GL.GenRenderbuffer();
        GL.BindRenderbuffer(RenderbufferTarget.Renderbuffer, _rbo);
        GL.RenderbufferStorage(RenderbufferTarget.Renderbuffer, RenderbufferStorage.Depth24Stencil8, width, height);
        GL.FramebufferRenderbuffer(FramebufferTarget.Framebuffer, FramebufferAttachment.DepthStencilAttachment,
            RenderbufferTarget.Renderbuffer, _rbo);

        GL.BindFramebuffer(FramebufferTarget.Framebuffer, 0);
    }

    public void Dispose()
    {
        GL.DeleteFramebuffer(Fbo);
        GL.DeleteTexture(ColorTexture);
        GL.DeleteRenderbuffer(_rbo);
    }
}