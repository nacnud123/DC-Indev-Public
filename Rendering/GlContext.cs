using Silk.NET.OpenGL;

namespace VoxelEngine.Rendering;

/// <summary>
/// Holds the one OpenGL context the whole game uses. Game.cs sets this once when the window is created; every other file just reads GlContext.Gl instead of passing the GL object everywhere. This is effectively a global service locator for the GL API surface: since Silk.NET's <see cref="GL"/> wrapper is a thin binding over the single OS-level GL context tied to the game window, there is only ever one meaningful instance for the lifetime of the process, so a static holder avoids threading a GL reference through every constructor and method in the Rendering/Terrain/UI code.
/// </summary>
internal static class GlContext
{
    // Set exactly once, in Game.Load, right after the Silk.NET window creates its GL context. Left non-nullable (`null!`) intentionally: every render-path file assumes this is always valid by the time it runs, and a null here would indicate a startup-order bug worth crashing loudly on.
    internal static GL Gl { get; set; } = null!;
}
