using Overcore.Core.Logging;
using Overcore.Rendering;
using Overcore.Utils;
using ImGuiNET;
using OpenTK.Graphics.OpenGL4;
using OpenTK.Mathematics;
using OpenTK.Windowing.GraphicsLibraryFramework;
using System.Runtime.CompilerServices;

namespace Overcore.Rendering.OpenTK;

public class ImGuiController : IUIController
{
    internal readonly record struct UIGlyph(float AdvanceX, float X0, float Y0, float X1, float Y1,
        float U0, float V0, float U1, float V1);

    private bool _frameBegun;

    private int _vertexArray;
    private int _vertexBuffer;
    private int _vertexBufferSize;
    private int _indexBuffer;
    private int _indexBufferSize;

    private int _fontTexture;
    private int _shader;
    private int _shaderFontTextureLocation;
    private int _shaderProjectionMatrixLocation;

    private int _windowWidth;
    private int _windowHeight;
    private readonly IntPtr _imguiContext;
    private readonly Dictionary<string, ImFontPtr> _loadedFonts = new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<string> _pendingFonts = new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<string> _failedFonts = new(StringComparer.OrdinalIgnoreCase);
    private ImFontPtr _defaultFont;
    private bool _disposed;

    private System.Numerics.Vector2 _scaleFactor = System.Numerics.Vector2.One;

    public ImGuiController(int width, int height, bool startFrame = true)
    {
        _windowWidth = width;
        _windowHeight = height;

        _imguiContext = ImGui.CreateContext();
        ImGui.SetCurrentContext(_imguiContext);
        ImGuiIOPtr io = ImGui.GetIO();

        const float baseFontSize = 18.0f;
        string? fontPath = ProjectFontAssets.Find(null);

        if (fontPath is not null && File.Exists(fontPath))
        {
            _defaultFont = io.Fonts.AddFontFromFileTTF(fontPath, baseFontSize);
        }
        else
        {
            Console.WriteLine("[ImGuiController] Bundled font not found. Using ImGui's default font.");
            _defaultFont = io.Fonts.AddFontDefault();
        }
        if (!String.IsNullOrWhiteSpace(fontPath))
        {
            _loadedFonts[Path.GetFullPath(fontPath)] = _defaultFont;
        }

        io.FontGlobalScale = 1.0f;

        io.BackendFlags |= ImGuiBackendFlags.RendererHasVtxOffset;
        io.ConfigFlags |= ImGuiConfigFlags.DockingEnable;

        CreateDeviceResources();
        SetPerFrameImGuiData(1f / 60f);

        if (startFrame)
        {
            ImGui.NewFrame();
            _frameBegun = true;
        }
    }

    internal int FontTextureHandle => _fontTexture;

    internal float GetFontPixelSize(string? fontName) => GetFont(fontName).FontSize;

    internal bool EnsureFont(string? fontName)
    {
        if (String.IsNullOrWhiteSpace(fontName))
        {
            return true;
        }

        string? path = ProjectFontAssets.Find(fontName);
        if (path is null || !File.Exists(path))
        {
            return false;
        }

        path = Path.GetFullPath(path);
        if (_loadedFonts.ContainsKey(path))
        {
            return true;
        }
        if (_failedFonts.Contains(path))
        {
            return false;
        }

        if (_frameBegun)
        {
            _pendingFonts.Add(path);
            return false;
        }

        return LoadFont(path);
    }

    private bool LoadFont(string path)
    {
        try
        {
            ImGui.SetCurrentContext(_imguiContext);
            ImFontPtr font = ImGui.GetIO().Fonts.AddFontFromFileTTF(path, 18f);
            if (font.NativePtr == IntPtr.Zero)
            {
                _failedFonts.Add(path);
                Logger.Warning($"[UI] Could not load font asset '{path}'; using the bundled font instead.");
                return false;
            }

            _loadedFonts[path] = font;
            RecreateFontDeviceTexture();
            return true;
        }
        catch (Exception exception)
        {
            _failedFonts.Add(path);
            Logger.Warning($"[UI] Could not load font asset '{path}': {exception.Message}");
            return false;
        }
    }

    internal bool TryGetGlyph(string? fontName, char codepoint, out UIGlyph glyph)
    {
        ImFontGlyphPtr source = GetFont(fontName).FindGlyph(codepoint);
        glyph = new UIGlyph(source.AdvanceX, source.X0, source.Y0, source.X1, source.Y1,
            source.U0, source.V0, source.U1, source.V1);
        return source.AdvanceX > 0f;
    }

    private ImFontPtr GetFont(string? fontName)
    {
        ImGui.SetCurrentContext(_imguiContext);
        if (!String.IsNullOrWhiteSpace(fontName) && ProjectFontAssets.Find(fontName) is { } path)
        {
            path = Path.GetFullPath(path);
            if (_loadedFonts.TryGetValue(path, out ImFontPtr font))
            {
                return font;
            }
        }

        return _defaultFont;
    }

    public void WindowResized(int width, int height)
    {
        _windowWidth = width;
        _windowHeight = height;
    }

    public void DestroyDeviceObjects()
    {
        if (_vertexArray != 0) GL.DeleteVertexArray(_vertexArray);
        if (_vertexBuffer != 0) GL.DeleteBuffer(_vertexBuffer);
        if (_indexBuffer != 0) GL.DeleteBuffer(_indexBuffer);
        if (_fontTexture != 0) GL.DeleteTexture(_fontTexture);
        if (_shader != 0) GL.DeleteProgram(_shader);
        _vertexArray = _vertexBuffer = _indexBuffer = _fontTexture = _shader = 0;
    }

    public void CreateDeviceResources()
    {
        _vertexBufferSize = 10000;
        _indexBufferSize = 2000;

        _vertexArray = GL.GenVertexArray();
        GL.BindVertexArray(_vertexArray);

        _vertexBuffer = GL.GenBuffer();
        GL.BindBuffer(BufferTarget.ArrayBuffer, _vertexBuffer);
        GL.BufferData(BufferTarget.ArrayBuffer, _vertexBufferSize, IntPtr.Zero, BufferUsageHint.DynamicDraw);

        _indexBuffer = GL.GenBuffer();
        GL.BindBuffer(BufferTarget.ElementArrayBuffer, _indexBuffer);
        GL.BufferData(BufferTarget.ElementArrayBuffer, _indexBufferSize, IntPtr.Zero, BufferUsageHint.DynamicDraw);

        RecreateFontDeviceTexture();

        string VertexSource = @"#version 330 core
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
        string FragmentSource = @"#version 330 core
uniform sampler2D in_fontTexture;
in vec4 color;
in vec2 texCoord;
out vec4 outputColor;
void main()
{
    outputColor = color * texture(in_fontTexture, texCoord);
}";

        _shader = CreateProgram("ImGui", VertexSource, FragmentSource);
        _shaderProjectionMatrixLocation = GL.GetUniformLocation(_shader, "projection_matrix");
        _shaderFontTextureLocation = GL.GetUniformLocation(_shader, "in_fontTexture");

        int stride = Unsafe.SizeOf<ImDrawVert>();
        GL.VertexAttribPointer(0, 2, VertexAttribPointerType.Float, false, stride, 0);
        GL.VertexAttribPointer(1, 2, VertexAttribPointerType.Float, false, stride, 8);
        GL.VertexAttribPointer(2, 4, VertexAttribPointerType.UnsignedByte, true, stride, 16);

        GL.EnableVertexAttribArray(0);
        GL.EnableVertexAttribArray(1);
        GL.EnableVertexAttribArray(2);

        GL.BindVertexArray(0);
        GL.BindBuffer(BufferTarget.ArrayBuffer, 0);
    }

    public void RecreateFontDeviceTexture()
    {
        ImGui.SetCurrentContext(_imguiContext);
        int previousTexture = _fontTexture;
        ImGuiIOPtr io = ImGui.GetIO();
        io.Fonts.GetTexDataAsRGBA32(out IntPtr pixels, out int width, out int height, out _);

        _fontTexture = GL.GenTexture();
        GL.BindTexture(TextureTarget.Texture2D, _fontTexture);
        GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureWrapS, (int)TextureWrapMode.Repeat);
        GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureWrapT, (int)TextureWrapMode.Repeat);
        GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMinFilter, (int)TextureMinFilter.Linear);
        GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMagFilter, (int)TextureMagFilter.Linear);
        GL.TexImage2D(TextureTarget.Texture2D, 0, PixelInternalFormat.Rgba, width, height, 0, PixelFormat.Bgra, PixelType.UnsignedByte, pixels);

        io.Fonts.SetTexID(_fontTexture);
        io.Fonts.ClearTexData();
        if (previousTexture != 0)
        {
            GL.DeleteTexture(previousTexture);
        }
    }

    public void Render()
    {
        ImGui.SetCurrentContext(_imguiContext);
        if (!_frameBegun)
        {
            return;
        }

        _frameBegun = false;
        ImGui.Render();
        RenderImDrawData(ImGui.GetDrawData());
    }

    public void Update(float deltaSeconds)
    {
        ImGui.SetCurrentContext(_imguiContext);
        if (_frameBegun)
        {
            ImGui.Render();
        }

        foreach (string fontPath in _pendingFonts.ToArray())
        {
            _pendingFonts.Remove(fontPath);
            LoadFont(fontPath);
        }

        SetPerFrameImGuiData(deltaSeconds);

        _frameBegun = true;
        ImGui.NewFrame();
    }

    private void SetPerFrameImGuiData(float deltaSeconds)
    {
        ImGui.SetCurrentContext(_imguiContext);
        ImGuiIOPtr io = ImGui.GetIO();

        io.DisplaySize = new(
            _windowWidth / _scaleFactor.X,
            _windowHeight / _scaleFactor.Y);

        io.DisplayFramebufferScale = _scaleFactor;
        io.DeltaTime = deltaSeconds;
    }

    public void MouseMove(Vector2 newPosition)
    {
        ImGui.SetCurrentContext(_imguiContext);
        ImGuiIOPtr io = ImGui.GetIO();
        io.AddMousePosEvent(newPosition.X, newPosition.Y);
    }

    public static void MouseScroll(Vector2 offset)
    {
        ImGuiIOPtr io = ImGui.GetIO();
        io.AddMouseWheelEvent(offset.X, offset.Y);
    }

    public void MouseWheel(Vector2 offset)
    {
        ImGui.SetCurrentContext(_imguiContext);
        ImGuiIOPtr io = ImGui.GetIO();
        io.AddMouseWheelEvent(offset.X, offset.Y);
    }

    public void MouseButton(global::OpenTK.Windowing.GraphicsLibraryFramework.MouseButton button, bool down)
    {
        ImGui.SetCurrentContext(_imguiContext);
        ImGuiIOPtr io = ImGui.GetIO();
        io.AddMouseButtonEvent((int)button, down);
    }

    public void PressChar(char keyChar)
    {
        ImGui.SetCurrentContext(_imguiContext);
        ImGuiIOPtr io = ImGui.GetIO();
        io.AddInputCharacter(keyChar);
    }

    public void KeyEvent(Keys key, bool isRepeat, bool down)
    {
        ImGui.SetCurrentContext(_imguiContext);
        ImGuiIOPtr io = ImGui.GetIO();

        if (s_KeyMap.TryGetValue(key, out ImGuiKey imguikey))
        {
            io.AddKeyEvent(imguikey, down);
        }
    }

    private unsafe void RenderImDrawData(ImDrawDataPtr draw_data)
    {
        if (draw_data.CmdListsCount == 0)
        {
            return;
        }

        GL.GetInteger(GetPName.ActiveTexture, out int lastActiveTexture);
        GL.ActiveTexture(TextureUnit.Texture0);

        GL.GetInteger(GetPName.CurrentProgram, out int lastProgram);
        GL.GetInteger(GetPName.ArrayBufferBinding, out int lastArrayBuffer);
        GL.GetInteger(GetPName.VertexArrayBinding, out int lastVertexArray);

        GL.Enable(EnableCap.Blend);
        GL.BlendEquation(BlendEquationMode.FuncAdd);
        GL.BlendFunc(BlendingFactor.SrcAlpha, BlendingFactor.OneMinusSrcAlpha);
        GL.Disable(EnableCap.CullFace);
        GL.Disable(EnableCap.DepthTest);
        GL.Enable(EnableCap.ScissorTest);

        GL.BindVertexArray(_vertexArray);

        Matrix4 projection = Matrix4.CreateOrthographicOffCenter(
            0f, _windowWidth,
            _windowHeight, 0.0f,
            -1.0f, 1.0f);

        GL.UseProgram(_shader);
        GL.UniformMatrix4(_shaderProjectionMatrixLocation, false, ref projection);
        GL.Uniform1(_shaderFontTextureLocation, 0);

        draw_data.ScaleClipRects(_scaleFactor);

        for (int i = 0; i < draw_data.CmdListsCount; i++)
        {
            ImDrawListPtr cmd_list = ((ImDrawListPtr*)draw_data.CmdLists)[i];

            GL.BindBuffer(BufferTarget.ArrayBuffer, _vertexBuffer);

            GL.BufferData(
                BufferTarget.ArrayBuffer,
                cmd_list.VtxBuffer.Size * Unsafe.SizeOf<ImDrawVert>(),
                cmd_list.VtxBuffer.Data,
                BufferUsageHint.StreamDraw);

            GL.BindBuffer(BufferTarget.ElementArrayBuffer, _indexBuffer);

            GL.BufferData(
                BufferTarget.ElementArrayBuffer,
                cmd_list.IdxBuffer.Size * sizeof(ushort),
                cmd_list.IdxBuffer.Data,
                BufferUsageHint.StreamDraw);

            for (int cmd_i = 0; cmd_i < cmd_list.CmdBuffer.Size; cmd_i++)
            {
                ImDrawCmdPtr pcmd = cmd_list.CmdBuffer[cmd_i];

                if (pcmd.UserCallback != IntPtr.Zero)
                {
                    throw new NotImplementedException();
                }

                GL.ActiveTexture(TextureUnit.Texture0);
                GL.BindTexture(TextureTarget.Texture2D, (int)pcmd.TextureId);

                System.Numerics.Vector4 clip = pcmd.ClipRect;
                GL.Scissor((int)clip.X, _windowHeight - (int)clip.W, (int)(clip.Z - clip.X), (int)(clip.W - clip.Y));

                GL.DrawElementsBaseVertex(
                    PrimitiveType.Triangles,
                    (int)pcmd.ElemCount,
                    DrawElementsType.UnsignedShort,
                    (IntPtr)(pcmd.IdxOffset * sizeof(ushort)),
                    (int)pcmd.VtxOffset);
            }
        }

        GL.Disable(EnableCap.Blend);
        GL.Disable(EnableCap.ScissorTest);

        GL.UseProgram(lastProgram);
        GL.ActiveTexture((TextureUnit)lastActiveTexture);
        GL.BindVertexArray(lastVertexArray);
        GL.BindBuffer(BufferTarget.ArrayBuffer, lastArrayBuffer);
    }

    private static int CreateProgram(string name, string vertexSource, string fragmentSource)
    {
        int program = GL.CreateProgram();

        int vertex = CompileShader(name, ShaderType.VertexShader, vertexSource);
        int fragment = CompileShader(name, ShaderType.FragmentShader, fragmentSource);

        GL.AttachShader(program, vertex);
        GL.AttachShader(program, fragment);

        GL.LinkProgram(program);

        GL.GetProgram(program, GetProgramParameterName.LinkStatus, out int success);
        if (success == 0)
        {
            string info = GL.GetProgramInfoLog(program);
            Console.WriteLine($"GL.LinkProgram had info log for '{name}':\n{info}");
        }

        GL.DetachShader(program, vertex);
        GL.DetachShader(program, fragment);

        GL.DeleteShader(vertex);
        GL.DeleteShader(fragment);

        return program;
    }

    private static int CompileShader(string name, ShaderType type, string source)
    {
        int shader = GL.CreateShader(type);
        GL.ShaderSource(shader, source);
        GL.CompileShader(shader);

        GL.GetShader(shader, ShaderParameter.CompileStatus, out int success);
        if (success == 0)
        {
            string info = GL.GetShaderInfoLog(shader);
            Console.WriteLine($"GL.CompileShader for '{name}' ({type}) had info log:\n{info}");
        }
        return shader;
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        ImGui.SetCurrentContext(_imguiContext);
        DestroyDeviceObjects();
        ImGui.DestroyContext(_imguiContext);
    }

    private static readonly Dictionary<Keys, ImGuiKey> s_KeyMap = new()
    {
        [Keys.Tab] = ImGuiKey.Tab,
        [Keys.Left] = ImGuiKey.LeftArrow,
        [Keys.Right] = ImGuiKey.RightArrow,
        [Keys.Up] = ImGuiKey.UpArrow,
        [Keys.Down] = ImGuiKey.DownArrow,
        [Keys.PageUp] = ImGuiKey.PageUp,
        [Keys.PageDown] = ImGuiKey.PageDown,
        [Keys.Home] = ImGuiKey.Home,
        [Keys.End] = ImGuiKey.End,
        [Keys.Insert] = ImGuiKey.Insert,
        [Keys.Delete] = ImGuiKey.Delete,
        [Keys.Backspace] = ImGuiKey.Backspace,
        [Keys.Space] = ImGuiKey.Space,
        [Keys.Enter] = ImGuiKey.Enter,
        [Keys.Escape] = ImGuiKey.Escape,
        [Keys.A] = ImGuiKey.A,
        [Keys.B] = ImGuiKey.B,
        [Keys.C] = ImGuiKey.C,
        [Keys.D] = ImGuiKey.D,
        [Keys.E] = ImGuiKey.E,
        [Keys.F] = ImGuiKey.F,
        [Keys.G] = ImGuiKey.G,
        [Keys.H] = ImGuiKey.H,
        [Keys.I] = ImGuiKey.I,
        [Keys.J] = ImGuiKey.J,
        [Keys.K] = ImGuiKey.K,
        [Keys.L] = ImGuiKey.L,
        [Keys.M] = ImGuiKey.M,
        [Keys.N] = ImGuiKey.N,
        [Keys.O] = ImGuiKey.O,
        [Keys.P] = ImGuiKey.P,
        [Keys.Q] = ImGuiKey.Q,
        [Keys.R] = ImGuiKey.R,
        [Keys.S] = ImGuiKey.S,
        [Keys.T] = ImGuiKey.T,
        [Keys.U] = ImGuiKey.U,
        [Keys.V] = ImGuiKey.V,
        [Keys.W] = ImGuiKey.W,
        [Keys.X] = ImGuiKey.X,
        [Keys.Y] = ImGuiKey.Y,
        [Keys.Z] = ImGuiKey.Z,
        [Keys.D0] = ImGuiKey._0,
        [Keys.D1] = ImGuiKey._1,
        [Keys.D2] = ImGuiKey._2,
        [Keys.D3] = ImGuiKey._3,
        [Keys.D4] = ImGuiKey._4,
        [Keys.D5] = ImGuiKey._5,
        [Keys.D6] = ImGuiKey._6,
        [Keys.D7] = ImGuiKey._7,
        [Keys.D8] = ImGuiKey._8,
        [Keys.D9] = ImGuiKey._9,
        [Keys.LeftShift] = ImGuiKey.LeftShift,
        [Keys.RightShift] = ImGuiKey.RightShift,
        [Keys.LeftControl] = ImGuiKey.LeftCtrl,
        [Keys.RightControl] = ImGuiKey.RightCtrl,
        [Keys.LeftAlt] = ImGuiKey.LeftAlt,
        [Keys.RightAlt] = ImGuiKey.RightAlt,
    };
}