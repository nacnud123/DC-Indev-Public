using ImGuiNET;
using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using Silk.NET.OpenGL;
using Silk.NET.Input;

using System.Diagnostics;
using VoxelEngine.Rendering;
using SilkKey = Silk.NET.Input.Key;

namespace VoxelEngine.UI;

/// <summary>
/// Hand-rolled ImGui.NET backend for Silk.NET (deliberately not the Silk.NET.OpenGL.Extensions.ImGui package, so the game controls exactly how ImGui's own GL resources and draw calls interact with the rest of the renderer). Owns ImGui's device objects (VAO/VBO/EBO/font texture/shader), feeds it per-frame input from Silk.NET's IMouse/IKeyboard, and issues its draw calls each frame. Because this runs interleaved with the world renderer (HUD/menus draw every frame on top of the 3D scene), RenderImDrawData is careful to save and restore any GL state it touches - see the comments there.
/// </summary>
public class ImGuiController : IDisposable
{
    private bool mFrameBegun;

    private uint mVertexArray;
    private uint mVertexBuffer;
    private int mVertexBufferSize;
    private uint mIndexBuffer;
    private int mIndexBufferSize;

    private uint mFontTexture;
    private uint mShader;
    private int mShaderFontTextureLocation;
    private int mShaderProjectionMatrixLocation;

    private int mWindowWidth;
    private int mWindowHeight;

    public static ImFontPtr fontNormal;
    public static ImFontPtr fontLarge;

    public ImGuiController(int width, int height)
    {
        mWindowWidth = width;
        mWindowHeight = height;

        IntPtr context = ImGui.CreateContext();
        ImGui.SetCurrentContext(context);
        var io = ImGui.GetIO();

        // Default font used by most UI text; kept as a static so screens can reference it without going through the controller instance.
        fontNormal = io.Fonts.AddFontDefault();

        unsafe
        {
            // Second font atlas entry at a larger fixed pixel size, used for titles/headers. Oversample 1 + PixelSnapH keeps it crisp at this exact size rather than smoothed.
            ImFontConfigPtr config = ImGuiNative.ImFontConfig_ImFontConfig();
            config.OversampleH = 1;
            config.OversampleV = 1;
            config.PixelSnapH = true;
            config.SizePixels = 26;
            fontLarge = io.Fonts.AddFontDefault(config);
        }

        // VtxOffset lets ImGui reuse a 16-bit index buffer across draw lists larger than 65536 vertices (via glDrawElementsBaseVertex) instead of forcing 32-bit indices everywhere.
        io.BackendFlags |= ImGuiBackendFlags.RendererHasVtxOffset;
        io.ConfigFlags |= ImGuiConfigFlags.DockingEnable;

        CreateDeviceResources();
        SetPerFrameImGuiData(1f / 60f);

        // Start the first ImGui frame immediately so screens can call ImGui.* before the first real Update() tick (e.g. during Game.Load).
        ImGui.NewFrame();
        mFrameBegun = true;
    }

    public void WindowResized(int width, int height)
    {
        mWindowWidth = width;
        mWindowHeight = height;
    }

    public void DestroyDeviceObjects() => Dispose();

    /// <summary>
    /// Allocates ImGui's own VAO/VBO/EBO, compiles its shader, and uploads the font atlas. Buffer sizes start small and are grown on demand in RenderImDrawData. Saves/restores the caller's VAO/VBO bindings so this can safely run mid-frame without disturbing whatever the game was about to bind next.
    /// </summary>
    public void CreateDeviceResources()
    {
        var gl = GlContext.Gl;
        mVertexBufferSize = 10000;
        mIndexBufferSize = 2000;

        gl.GetInteger(GetPName.VertexArrayBinding, out int prevVao);
        gl.GetInteger(GetPName.ArrayBufferBinding, out int prevArrayBuffer);

        mVertexArray = gl.GenVertexArray();
        gl.BindVertexArray(mVertexArray);
        LabelObject(ObjectIdentifier.VertexArray, mVertexArray, "ImGui");

        mVertexBuffer = gl.GenBuffer();
        gl.BindBuffer(BufferTargetARB.ArrayBuffer, mVertexBuffer);
        LabelObject(ObjectIdentifier.Buffer, mVertexBuffer, "VBO: ImGui");
        unsafe { gl.BufferData(BufferTargetARB.ArrayBuffer, (nuint)mVertexBufferSize, null, BufferUsageARB.DynamicDraw); }

        mIndexBuffer = gl.GenBuffer();
        gl.BindBuffer(BufferTargetARB.ElementArrayBuffer, mIndexBuffer);
        LabelObject(ObjectIdentifier.Buffer, mIndexBuffer, "EBO: ImGui");
        unsafe { gl.BufferData(BufferTargetARB.ElementArrayBuffer, (nuint)mIndexBufferSize, null, BufferUsageARB.DynamicDraw); }

        RecreateFontDeviceTexture();

        string vertexSource = @"#version 330 core

uniform mat4 projection_matrix;

layout(location = 0) in vec2 in_position;
layout(location = 1) in vec2 in_texCoord;
layout(location = 2) in vec4 in_color;

out vec4 color;
out vec2 texCoord;

void main()
{
    gl_Position = projection_matrix * vec4(in_position, 0, 1);
    color = in_color;
    texCoord = in_texCoord;
}";
        string fragmentSource = @"#version 330 core

uniform sampler2D in_fontTexture;

in vec4 color;
in vec2 texCoord;

out vec4 outputColor;

void main()
{
    outputColor = color * texture(in_fontTexture, texCoord);
}";

        mShader = CreateProgram("ImGui", vertexSource, fragmentSource);
        mShaderProjectionMatrixLocation = gl.GetUniformLocation(mShader, "projection_matrix");
        mShaderFontTextureLocation = gl.GetUniformLocation(mShader, "in_fontTexture");

        // ImDrawVert's layout is fixed by ImGui itself: 2 floats position, 2 floats UV, 4 bytes RGBA color (packed, hence UnsignedByte + normalize=true). Offsets below match that struct.
        uint stride = (uint)Unsafe.SizeOf<ImDrawVert>();
        gl.VertexAttribPointer(0, 2, GLEnum.Float, false, stride, 0);
        gl.VertexAttribPointer(1, 2, GLEnum.Float, false, stride, 8);
        gl.VertexAttribPointer(2, 4, VertexAttribPointerType.UnsignedByte, true, stride, 16);
        gl.EnableVertexAttribArray(0);
        gl.EnableVertexAttribArray(1);
        gl.EnableVertexAttribArray(2);

        gl.BindVertexArray((uint)prevVao);
        gl.BindBuffer(BufferTargetARB.ArrayBuffer, (uint)prevArrayBuffer);
    }

    /// <summary>
    /// Rasterizes ImGui's font atlas (all registered fonts packed into one bitmap) into a GL texture. Called once at startup; would need to be called again if fonts were added/changed at runtime.
    /// </summary>
    public void RecreateFontDeviceTexture()
    {
        var gl = GlContext.Gl;
        ImGuiIOPtr io = ImGui.GetIO();
        io.Fonts.GetTexDataAsRGBA32(out IntPtr pixels, out int width, out int height, out int bytesPerPixel);

        // Full mip chain down to 1x1, matching the atlas's largest dimension.
        int mips = (int)Math.Floor(Math.Log(Math.Max(width, height), 2));

        gl.GetInteger(GetPName.ActiveTexture, out int prevActiveTexture);
        gl.ActiveTexture(TextureUnit.Texture0);
        gl.GetInteger(GetPName.TextureBinding2D, out int prevTexture2D);

        mFontTexture = gl.GenTexture();
        gl.BindTexture(TextureTarget.Texture2D, mFontTexture);
        gl.TexStorage2D(TextureTarget.Texture2D, (uint)mips, SizedInternalFormat.Rgba8, (uint)width, (uint)height);
        LabelObject(ObjectIdentifier.Texture, mFontTexture, "ImGui Text Atlas");

        unsafe { gl.TexSubImage2D(TextureTarget.Texture2D, 0, 0, 0, (uint)width, (uint)height, PixelFormat.Bgra, PixelType.UnsignedByte, (void*)pixels); }

        gl.GenerateMipmap(TextureTarget.Texture2D);

        gl.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureWrapS, (int)GLEnum.Repeat);
        gl.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureWrapT, (int)GLEnum.Repeat);
        gl.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMaxLevel, mips - 1);
        gl.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMagFilter, (int)GLEnum.Linear);
        gl.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMinFilter, (int)GLEnum.Linear);

        gl.BindTexture(TextureTarget.Texture2D, (uint)prevTexture2D);
        gl.ActiveTexture((TextureUnit)prevActiveTexture);

        io.Fonts.SetTexID(new IntPtr(mFontTexture));
        io.Fonts.ClearTexData();
    }

    /// <summary>Ends the current ImGui frame (if one is open) and draws it via GL.</summary>
    public void Render()
    {
        if (mFrameBegun)
        {
            mFrameBegun = false;
            ImGui.Render();
            RenderImDrawData(ImGui.GetDrawData());
        }
    }

    /// <summary>
    /// Called once per game tick before any ImGui.* UI code runs. Closes out the previous frame if Render() wasn't called on it (defensive - avoids ImGui asserting on NewFrame-without-Render), pushes fresh input state, and opens the next frame.
    /// </summary>
    public void Update(float deltaSeconds, IMouse mouse, IKeyboard keyboard)
    {
        if (mFrameBegun) ImGui.Render();
        SetPerFrameImGuiData(deltaSeconds);
        UpdateImGuiInput(mouse, keyboard);
        mFrameBegun = true;
        ImGui.NewFrame();
    }

    private void SetPerFrameImGuiData(float deltaSeconds)
    {
        ImGuiIOPtr io = ImGui.GetIO();
        io.DisplaySize = new System.Numerics.Vector2(mWindowWidth, mWindowHeight);
        io.DisplayFramebufferScale = System.Numerics.Vector2.One;
        io.DeltaTime = deltaSeconds;
    }

    // Text-input characters queued by PressChar (fed from the window's character-typed event) and flushed into ImGui's IO once per Update - keeps this decoupled from the raw key-down events used for TranslateKey below, since ImGui wants Unicode text input separately from key codes.
    readonly List<char> mPressedChars = new();

    private void UpdateImGuiInput(IMouse mouse, IKeyboard keyboard)
    {
        ImGuiIOPtr io = ImGui.GetIO();

        io.MouseDown[0] = mouse.IsButtonPressed(Silk.NET.Input.MouseButton.Left);
        io.MouseDown[1] = mouse.IsButtonPressed(Silk.NET.Input.MouseButton.Right);
        io.MouseDown[2] = mouse.IsButtonPressed(Silk.NET.Input.MouseButton.Middle);

        io.MousePos = new System.Numerics.Vector2(mouse.Position.X, mouse.Position.Y);

        // Every frame, walk the full Silk.NET Key enum and tell ImGui the down/up state of each one. This is a polling approach (not event-driven), so it's simple but does mean ImGui itself handles edge-detection (e.g. "was this key just pressed this frame") internally.
        foreach (SilkKey key in Enum.GetValues<SilkKey>())
        {
            if (key == SilkKey.Unknown) continue;
            io.AddKeyEvent(TranslateKey(key), keyboard.IsKeyPressed(key));
        }

        foreach (var c in mPressedChars) io.AddInputCharacter(c);
        mPressedChars.Clear();

        io.KeyCtrl = keyboard.IsKeyPressed(SilkKey.ControlLeft) || keyboard.IsKeyPressed(SilkKey.ControlRight);
        io.KeyAlt = keyboard.IsKeyPressed(SilkKey.AltLeft) || keyboard.IsKeyPressed(SilkKey.AltRight);
        io.KeyShift = keyboard.IsKeyPressed(SilkKey.ShiftLeft) || keyboard.IsKeyPressed(SilkKey.ShiftRight);
        io.KeySuper = keyboard.IsKeyPressed(SilkKey.SuperLeft) || keyboard.IsKeyPressed(SilkKey.SuperRight);
    }

    internal void PressChar(char keyChar) => mPressedChars.Add(keyChar);

    internal void MouseScroll(Vector2 offset)
    {
        ImGuiIOPtr io = ImGui.GetIO();
        io.MouseWheel = offset.Y;
        io.MouseWheelH = offset.X;
    }

    /// <summary>
    /// Translates ImGui's recorded draw commands (vertex/index buffers + per-command clip rects and textures) into actual GL calls. This runs every frame after the world/entities/HUD have already rendered, so it must not leave GL in a state the *next* frame's world render doesn't expect - every piece of state this method changes (bindings, blend/cull/depth/scissor toggles, polygon mode, active texture unit) is queried up front and restored at the end. This save/ restore discipline is what the wireframe-toggle bug taught us matters: this method used to force PolygonMode to Fill without saving/restoring it, which silently undid the game's wireframe debug toggle every single frame since this runs after the toggle's effect. The prevPolygonMode save/restore pair below is the fix.
    /// </summary>
    private void RenderImDrawData(ImDrawDataPtr drawData)
    {
        if (drawData.CmdListsCount == 0) return;

        var gl = GlContext.Gl;

        // --- Snapshot every piece of GL state this method is about to touch ---
        gl.GetInteger(GetPName.VertexArrayBinding, out int prevVao);
        gl.GetInteger(GetPName.ArrayBufferBinding, out int prevArrayBuffer);
        gl.GetInteger(GetPName.CurrentProgram, out int prevProgram);
        gl.GetInteger(GetPName.BlendEquationRgb, out int prevBlendEquationRgb);
        gl.GetInteger(GetPName.BlendEquationAlpha, out int prevBlendEquationAlpha);
        gl.GetInteger(GetPName.BlendSrcRgb, out int prevBlendSrcRgb);
        gl.GetInteger(GetPName.BlendSrcAlpha, out int prevBlendSrcAlpha);
        gl.GetInteger(GetPName.BlendDstRgb, out int prevBlendDstRgb);
        gl.GetInteger(GetPName.BlendDstAlpha, out int prevBlendDstAlpha);
        gl.GetInteger(GetPName.ActiveTexture, out int prevActiveTexture);
        gl.GetInteger(GetPName.TextureBinding2D, out int prevTexture2D);
        gl.GetBoolean(GetPName.Blend, out bool prevBlendEnabled);
        gl.GetBoolean(GetPName.ScissorTest, out bool prevScissorTestEnabled);
        gl.GetBoolean(GetPName.CullFace, out bool prevCullFaceEnabled);
        gl.GetBoolean(GetPName.DepthTest, out bool prevDepthTestEnabled);

        Span<int> prevScissorBox = stackalloc int[4];
        unsafe { fixed (int* p = prevScissorBox) { gl.GetInteger(GetPName.ScissorBox, p); } }

        // PolygonMode is queried as 2 ints (front-face mode, back-face mode) even though the game always sets both faces together via TriangleFace.FrontAndBack - GL_POLYGON_MODE just always reports both slots, so index [0] is enough to restore correctly below.
        Span<int> prevPolygonMode = stackalloc int[2];
        unsafe { fixed (int* p = prevPolygonMode) { gl.GetInteger(GetPName.PolygonMode, p); } }

        // ImGui is always drawn filled/solid regardless of the game's wireframe debug toggle - wireframe mode is meant to visualize world geometry, not the UI on top of it.
        gl.PolygonMode(TriangleFace.FrontAndBack, PolygonMode.Fill);
        gl.ActiveTexture(TextureUnit.Texture0);

        gl.BindVertexArray(mVertexArray);
        gl.BindBuffer(BufferTargetARB.ArrayBuffer, mVertexBuffer);

        // Grow the shared vertex/index buffers (1.5x) if this frame's UI needs more room than last frame's allocation - avoids reallocating every frame once a high-water mark is hit.
        for (int i = 0; i < drawData.CmdListsCount; i++)
        {
            ImDrawListPtr cmdList = drawData.CmdLists[i];

            int vertexSize = cmdList.VtxBuffer.Size * Unsafe.SizeOf<ImDrawVert>();
            if (vertexSize > mVertexBufferSize)
            {
                int newSize = (int)Math.Max(mVertexBufferSize * 1.5f, vertexSize);
                unsafe { gl.BufferData(BufferTargetARB.ArrayBuffer, (nuint)newSize, null, BufferUsageARB.DynamicDraw); }
                mVertexBufferSize = newSize;
            }

            int indexSize = cmdList.IdxBuffer.Size * sizeof(ushort);
            if (indexSize > mIndexBufferSize)
            {
                int newSize = (int)Math.Max(mIndexBufferSize * 1.5f, indexSize);
                unsafe { gl.BufferData(BufferTargetARB.ElementArrayBuffer, (nuint)newSize, null, BufferUsageARB.DynamicDraw); }
                mIndexBufferSize = newSize;
            }
        }

        ImGuiIOPtr io = ImGui.GetIO();
        // Simple 2D orthographic projection in screen pixels, Y-down (top=0) to match ImGui's own screen-space coordinate convention rather than GL's usual bottom-left origin.
        Matrix4x4 mvp = Matrix4x4.CreateOrthographicOffCenter(0f, io.DisplaySize.X, io.DisplaySize.Y, 0f, -1f, 1f);

        gl.UseProgram(mShader);
        gl.UniformMatrix4(mShaderProjectionMatrixLocation, 1, false,
            MemoryMarshal.Cast<Matrix4x4, float>(MemoryMarshal.CreateSpan(ref mvp, 1)));
        gl.Uniform1(mShaderFontTextureLocation, 0);

        gl.BindVertexArray(mVertexArray);
        drawData.ScaleClipRects(io.DisplayFramebufferScale);

        // Standard alpha-blended, unculled, non-depth-tested UI rendering: text/icons need blending, 2D quads have no "back face" to cull, and UI should always draw on top regardless of depth.
        gl.Enable(EnableCap.Blend);
        gl.Enable(EnableCap.ScissorTest);
        gl.BlendEquation(BlendEquationModeEXT.FuncAdd);
        gl.BlendFunc(BlendingFactor.SrcAlpha, BlendingFactor.OneMinusSrcAlpha);
        gl.Disable(EnableCap.CullFace);
        gl.Disable(EnableCap.DepthTest);

        // One draw call per ImGui draw-list (roughly: one per ImGui window/layer), each potentially spanning multiple ImDrawCmd entries (one per distinct texture/clip-rect within that list).
        for (int n = 0; n < drawData.CmdListsCount; n++)
        {
            ImDrawListPtr cmdList = drawData.CmdLists[n];

            unsafe
            {
                gl.BufferSubData(BufferTargetARB.ArrayBuffer, 0, (nuint)(cmdList.VtxBuffer.Size * Unsafe.SizeOf<ImDrawVert>()), (void*)cmdList.VtxBuffer.Data);
                gl.BufferSubData(BufferTargetARB.ElementArrayBuffer, 0, (nuint)(cmdList.IdxBuffer.Size * sizeof(ushort)), (void*)cmdList.IdxBuffer.Data);
            }

            for (int cmdI = 0; cmdI < cmdList.CmdBuffer.Size; cmdI++)
            {
                ImDrawCmdPtr pcmd = cmdList.CmdBuffer[cmdI];
                if (pcmd.UserCallback != IntPtr.Zero)
                    throw new NotImplementedException();

                gl.ActiveTexture(TextureUnit.Texture0);
                gl.BindTexture(TextureTarget.Texture2D, (uint)(nint)pcmd.TextureId);

                // ImGui's clip rect is in screen space with Y-down/top-left origin; GL's scissor box is Y-up/bottom-left origin, hence flipping via (mWindowHeight - clip.W).
                var clip = pcmd.ClipRect;
                gl.Scissor((int)clip.X, mWindowHeight - (int)clip.W, (uint)(clip.Z - clip.X), (uint)(clip.W - clip.Y));

                unsafe
                {
                    // BaseVertex draw path lets each draw command reference vertices at an offset into the shared buffer without ImGui having to rebase indices itself - only available/used when RendererHasVtxOffset was advertised in Update() above.
                    if ((io.BackendFlags & ImGuiBackendFlags.RendererHasVtxOffset) != 0)
                        gl.DrawElementsBaseVertex(PrimitiveType.Triangles, pcmd.ElemCount, DrawElementsType.UnsignedShort, (void*)(pcmd.IdxOffset * sizeof(ushort)), (int)pcmd.VtxOffset);
                    else
                        gl.DrawElements(PrimitiveType.Triangles, pcmd.ElemCount, DrawElementsType.UnsignedShort, (void*)(pcmd.IdxOffset * sizeof(ushort)));
                }
            }
        }

        gl.Disable(EnableCap.Blend);
        gl.Disable(EnableCap.ScissorTest);

        // --- Restore every piece of state snapshotted above, in no particular order, so the next frame's world render (and any wireframe/other debug toggles) sees GL exactly as it left it ---
        gl.BindTexture(TextureTarget.Texture2D, (uint)prevTexture2D);
        gl.ActiveTexture((TextureUnit)prevActiveTexture);
        gl.UseProgram((uint)prevProgram);
        gl.BindVertexArray((uint)prevVao);
        gl.Scissor(prevScissorBox[0], prevScissorBox[1], (uint)prevScissorBox[2], (uint)prevScissorBox[3]);
        gl.BindBuffer(BufferTargetARB.ArrayBuffer, (uint)prevArrayBuffer);
        gl.BlendEquationSeparate((BlendEquationModeEXT)prevBlendEquationRgb, (BlendEquationModeEXT)prevBlendEquationAlpha);
        gl.BlendFuncSeparate((BlendingFactor)prevBlendSrcRgb, (BlendingFactor)prevBlendDstRgb, (BlendingFactor)prevBlendSrcAlpha, (BlendingFactor)prevBlendDstAlpha);
        if (prevBlendEnabled) gl.Enable(EnableCap.Blend); else gl.Disable(EnableCap.Blend);
        if (prevDepthTestEnabled) gl.Enable(EnableCap.DepthTest); else gl.Disable(EnableCap.DepthTest);
        if (prevCullFaceEnabled) gl.Enable(EnableCap.CullFace); else gl.Disable(EnableCap.CullFace);
        if (prevScissorTestEnabled) gl.Enable(EnableCap.ScissorTest); else gl.Disable(EnableCap.ScissorTest);
        gl.PolygonMode(TriangleFace.FrontAndBack, (PolygonMode)prevPolygonMode[0]);
    }

    /// <summary>Deletes all GL objects owned by this controller (VAO, VBO, EBO, font texture, shader program).</summary>
    public void Dispose()
    {
        var gl = GlContext.Gl;
        gl.DeleteVertexArray(mVertexArray);
        gl.DeleteBuffer(mVertexBuffer);
        gl.DeleteBuffer(mIndexBuffer);
        gl.DeleteTexture(mFontTexture);
        gl.DeleteProgram(mShader);
    }

    /// <summary>Attaches a debug label to a GL object (visible in graphics debuggers like RenderDoc/apitrace). Best-effort - swallows errors on drivers without KHR_debug support.</summary>
    public static void LabelObject(ObjectIdentifier objLabelIdent, uint glObject, string name)
    {
        try { GlContext.Gl.ObjectLabel(objLabelIdent, glObject, (uint)name.Length, name); } catch { }
    }

    /// <summary>Drains and logs the GL error queue. Call after a suspect GL call during debugging; a no-op in normal operation since GetError() returns NoError immediately when nothing's wrong.</summary>
    public static void CheckGlError(string title)
    {
        var gl = GlContext.Gl;
        GLEnum error;
        int i = 1;
        while ((error = gl.GetError()) != GLEnum.NoError)
            Debug.Print($"{title} ({i++}): {error}");
    }

    /// <summary>Compiles and links a vertex+fragment shader pair into a GL program, logging compile/link errors by name.</summary>
    public static uint CreateProgram(string name, string vertexSource, string fragmentSource)
    {
        var gl = GlContext.Gl;
        uint program = gl.CreateProgram();

        uint vertex = CompileShader(name, ShaderType.VertexShader, vertexSource);
        uint fragment = CompileShader(name, ShaderType.FragmentShader, fragmentSource);

        gl.AttachShader(program, vertex);
        gl.AttachShader(program, fragment);
        gl.LinkProgram(program);

        gl.GetProgram(program, ProgramPropertyARB.LinkStatus, out int success);
        if (success == 0)
        {
            string info = gl.GetProgramInfoLog(program);
            Debug.WriteLine($"LinkProgram [{name}]:\n{info}");
        }

        gl.DetachShader(program, vertex);
        gl.DetachShader(program, fragment);
        gl.DeleteShader(vertex);
        gl.DeleteShader(fragment);

        return program;
    }

    private static uint CompileShader(string name, ShaderType type, string source)
    {
        var gl = GlContext.Gl;
        uint shader = gl.CreateShader(type);
        gl.ShaderSource(shader, source);
        gl.CompileShader(shader);

        gl.GetShader(shader, ShaderParameterName.CompileStatus, out int success);
        if (success == 0)
        {
            string info = gl.GetShaderInfoLog(shader);
            Debug.WriteLine($"CompileShader [{name}/{type}]:\n{info}");
        }

        return shader;
    }

    /// <summary>
    /// Maps a Silk.NET key to ImGui's own key enum. Contiguous ranges (digits, letters, keypad, function keys) are mapped by offset arithmetic since both enums keep those runs in order; everything else falls through to an explicit lookup table below.
    /// </summary>
    public static ImGuiKey TranslateKey(SilkKey key)
    {
        if (key >= SilkKey.Number0 && key <= SilkKey.Number9)
            return key - SilkKey.Number0 + ImGuiKey._0;

        if (key >= SilkKey.A && key <= SilkKey.Z)
            return key - SilkKey.A + ImGuiKey.A;

        if (key >= SilkKey.Keypad0 && key <= SilkKey.Keypad9)
            return key - SilkKey.Keypad0 + ImGuiKey.Keypad0;

        if (key >= SilkKey.F1 && key <= SilkKey.F24)
            return key - SilkKey.F1 + ImGuiKey.F1;

        return key switch
        {
            SilkKey.Tab => ImGuiKey.Tab,
            SilkKey.Left => ImGuiKey.LeftArrow,
            SilkKey.Right => ImGuiKey.RightArrow,
            SilkKey.Up => ImGuiKey.UpArrow,
            SilkKey.Down => ImGuiKey.DownArrow,
            SilkKey.PageUp => ImGuiKey.PageUp,
            SilkKey.PageDown => ImGuiKey.PageDown,
            SilkKey.Home => ImGuiKey.Home,
            SilkKey.End => ImGuiKey.End,
            SilkKey.Insert => ImGuiKey.Insert,
            SilkKey.Delete => ImGuiKey.Delete,
            SilkKey.Backspace => ImGuiKey.Backspace,
            SilkKey.Space => ImGuiKey.Space,
            SilkKey.Enter => ImGuiKey.Enter,
            SilkKey.Escape => ImGuiKey.Escape,
            SilkKey.Apostrophe => ImGuiKey.Apostrophe,
            SilkKey.Comma => ImGuiKey.Comma,
            SilkKey.Minus => ImGuiKey.Minus,
            SilkKey.Period => ImGuiKey.Period,
            SilkKey.Slash => ImGuiKey.Slash,
            SilkKey.Semicolon => ImGuiKey.Semicolon,
            SilkKey.Equal => ImGuiKey.Equal,
            SilkKey.LeftBracket => ImGuiKey.LeftBracket,
            SilkKey.BackSlash => ImGuiKey.Backslash,
            SilkKey.RightBracket => ImGuiKey.RightBracket,
            SilkKey.GraveAccent => ImGuiKey.GraveAccent,
            SilkKey.CapsLock => ImGuiKey.CapsLock,
            SilkKey.ScrollLock => ImGuiKey.ScrollLock,
            SilkKey.NumLock => ImGuiKey.NumLock,
            SilkKey.PrintScreen => ImGuiKey.PrintScreen,
            SilkKey.Pause => ImGuiKey.Pause,
            SilkKey.KeypadDecimal => ImGuiKey.KeypadDecimal,
            SilkKey.KeypadDivide => ImGuiKey.KeypadDivide,
            SilkKey.KeypadMultiply => ImGuiKey.KeypadMultiply,
            SilkKey.KeypadSubtract => ImGuiKey.KeypadSubtract,
            SilkKey.KeypadAdd => ImGuiKey.KeypadAdd,
            SilkKey.KeypadEnter => ImGuiKey.KeypadEnter,
            SilkKey.ShiftLeft => ImGuiKey.LeftShift,
            SilkKey.ControlLeft => ImGuiKey.LeftCtrl,
            SilkKey.AltLeft => ImGuiKey.LeftAlt,
            SilkKey.SuperLeft => ImGuiKey.LeftSuper,
            SilkKey.ShiftRight => ImGuiKey.RightShift,
            SilkKey.ControlRight => ImGuiKey.RightCtrl,
            SilkKey.AltRight => ImGuiKey.RightAlt,
            SilkKey.SuperRight => ImGuiKey.RightSuper,
            SilkKey.Menu => ImGuiKey.Menu,
            _ => ImGuiKey.None
        };
    }
}
