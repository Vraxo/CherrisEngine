using Cherris.Rendering;
using ImGuiNET;
using OpenTK.Graphics.OpenGL4;
using OpenTK.Mathematics;
using OpenTK.Windowing.GraphicsLibraryFramework;
using System.Runtime.CompilerServices;

namespace Cherris.OpenTK
{
    public class ImGuiController : IUIController
    {
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

        private System.Numerics.Vector2 _scaleFactor = System.Numerics.Vector2.One;

        public ImGuiController(int width, int height)
        {
            _windowWidth = width;
            _windowHeight = height;

            IntPtr context = ImGui.CreateContext();
            ImGui.SetCurrentContext(context);
            var io = ImGui.GetIO();

            // --- FONT LOADING LOGIC (SIMPLE AND ROBUST) ---
            // After a full dependency clean, this is the correct and simplest approach.
            // If the font isn't found, ImGui will fall back to its default.
            // The crash was not due to file loading, but a native library mismatch.
            const float baseFontSize = 18.0f;
            string? fontPath = AssetFinder.FindAssetPath("Fonts/RobotoMono-Regular.ttf");

            if (fontPath is not null && File.Exists(fontPath))
            {
                io.Fonts.AddFontFromFileTTF(fontPath, baseFontSize);
            }
            else
            {
                Console.WriteLine($"[ImGuiController] Warning: Custom font not found. Using default font.");
                io.Fonts.AddFontDefault();
            }

            io.FontGlobalScale = 1.0f;
            // --- END FONT LOADING ---

            io.BackendFlags |= ImGuiBackendFlags.RendererHasVtxOffset;
            io.ConfigFlags |= ImGuiConfigFlags.DockingEnable;

            CreateDeviceResources();
            SetPerFrameImGuiData(1f / 60f);

            ImGui.NewFrame();
            _frameBegun = true;
        }

        public void WindowResized(int width, int height)
        {
            _windowWidth = width;
            _windowHeight = height;
        }

        public void DestroyDeviceObjects()
        {
            GL.DeleteVertexArray(_vertexArray);
            GL.DeleteBuffer(_vertexBuffer);
            GL.DeleteBuffer(_indexBuffer);
            GL.DeleteTexture(_fontTexture);
            GL.DeleteProgram(_shader);
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
            ImGuiIOPtr io = ImGui.GetIO();
            io.Fonts.GetTexDataAsRGBA32(out IntPtr pixels, out int width, out int height, out int bytesPerPixel);

            _fontTexture = GL.GenTexture();
            GL.BindTexture(TextureTarget.Texture2D, _fontTexture);
            GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureWrapS, (int)TextureWrapMode.Repeat);
            GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureWrapT, (int)TextureWrapMode.Repeat);
            GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMinFilter, (int)TextureMinFilter.Linear);
            GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMagFilter, (int)TextureMagFilter.Linear);
            GL.TexImage2D(TextureTarget.Texture2D, 0, PixelInternalFormat.Rgba, width, height, 0, PixelFormat.Bgra, PixelType.UnsignedByte, pixels);

            io.Fonts.SetTexID((IntPtr)_fontTexture);
            io.Fonts.ClearTexData();
        }

        public void Render()
        {
            if (_frameBegun)
            {
                _frameBegun = false;
                ImGui.Render();
                RenderImDrawData(ImGui.GetDrawData());
            }
        }

        public void Update(float deltaSeconds)
        {
            if (_frameBegun)
            {
                ImGui.Render();
            }

            SetPerFrameImGuiData(deltaSeconds);

            _frameBegun = true;
            ImGui.NewFrame();
        }

        private void SetPerFrameImGuiData(float deltaSeconds)
        {
            ImGuiIOPtr io = ImGui.GetIO();
            io.DisplaySize = new System.Numerics.Vector2(
                _windowWidth / _scaleFactor.X,
                _windowHeight / _scaleFactor.Y);
            io.DisplayFramebufferScale = _scaleFactor;
            io.DeltaTime = deltaSeconds;
        }

        public void MouseMove(Vector2 newPosition)
        {
            ImGuiIOPtr io = ImGui.GetIO();
            io.AddMousePosEvent(newPosition.X, newPosition.Y);
        }

        public void MouseScroll(Vector2 offset)
        {
            ImGuiIOPtr io = ImGui.GetIO();
            io.AddMouseWheelEvent(offset.X, offset.Y);
        }

        public void MouseButton(global::OpenTK.Windowing.GraphicsLibraryFramework.MouseButton button, bool down)
        {
            ImGuiIOPtr io = ImGui.GetIO();
            io.AddMouseButtonEvent((int)button, down);
        }

        public void PressChar(char keyChar)
        {
            ImGuiIOPtr io = ImGui.GetIO();
            io.AddInputCharacter(keyChar);
        }

        public void KeyEvent(Keys key, bool isRepeat, bool down)
        {
            ImGuiIOPtr io = ImGui.GetIO();
            if (s_KeyMap.TryGetValue(key, out var imguikey))
            {
                io.AddKeyEvent(imguikey, down);
            }
        }

        private unsafe void RenderImDrawData(ImDrawDataPtr draw_data)
        {
            if (draw_data.CmdListsCount == 0) return;

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
                GL.BufferData(BufferTarget.ArrayBuffer, cmd_list.VtxBuffer.Size * Unsafe.SizeOf<ImDrawVert>(), cmd_list.VtxBuffer.Data, BufferUsageHint.StreamDraw);

                GL.BindBuffer(BufferTarget.ElementArrayBuffer, _indexBuffer);
                GL.BufferData(BufferTarget.ElementArrayBuffer, cmd_list.IdxBuffer.Size * sizeof(ushort), cmd_list.IdxBuffer.Data, BufferUsageHint.StreamDraw);

                for (int cmd_i = 0; cmd_i < cmd_list.CmdBuffer.Size; cmd_i++)
                {
                    ImDrawCmdPtr pcmd = cmd_list.CmdBuffer[cmd_i];
                    if (pcmd.UserCallback != IntPtr.Zero)
                    {
                        throw new NotImplementedException();
                    }

                    GL.ActiveTexture(TextureUnit.Texture0);
                    GL.BindTexture(TextureTarget.Texture2D, (int)pcmd.TextureId);

                    var clip = pcmd.ClipRect;
                    GL.Scissor((int)clip.X, _windowHeight - (int)clip.W, (int)(clip.Z - clip.X), (int)(clip.W - clip.Y));

                    GL.DrawElementsBaseVertex(PrimitiveType.Triangles, (int)pcmd.ElemCount, DrawElementsType.UnsignedShort, (IntPtr)(pcmd.IdxOffset * sizeof(ushort)), (int)pcmd.VtxOffset);
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
            DestroyDeviceObjects();
        }

        private static readonly Dictionary<Keys, ImGuiKey> s_KeyMap = new()
        {
            { Keys.Tab, ImGuiKey.Tab },
            { Keys.Left, ImGuiKey.LeftArrow },
            { Keys.Right, ImGuiKey.RightArrow },
            { Keys.Up, ImGuiKey.UpArrow },
            { Keys.Down, ImGuiKey.DownArrow },
            { Keys.PageUp, ImGuiKey.PageUp },
            { Keys.PageDown, ImGuiKey.PageDown },
            { Keys.Home, ImGuiKey.Home },
            { Keys.End, ImGuiKey.End },
            { Keys.Insert, ImGuiKey.Insert },
            { Keys.Delete, ImGuiKey.Delete },
            { Keys.Backspace, ImGuiKey.Backspace },
            { Keys.Space, ImGuiKey.Space },
            { Keys.Enter, ImGuiKey.Enter },
            { Keys.Escape, ImGuiKey.Escape },
            { Keys.A, ImGuiKey.A }, { Keys.B, ImGuiKey.B }, { Keys.C, ImGuiKey.C }, { Keys.D, ImGuiKey.D },
            { Keys.E, ImGuiKey.E }, { Keys.F, ImGuiKey.F }, { Keys.G, ImGuiKey.G }, { Keys.H, ImGuiKey.H },
            { Keys.I, ImGuiKey.I }, { Keys.J, ImGuiKey.J }, { Keys.K, ImGuiKey.K }, { Keys.L, ImGuiKey.L },
            { Keys.M, ImGuiKey.M }, { Keys.N, ImGuiKey.N }, { Keys.O, ImGuiKey.O }, { Keys.P, ImGuiKey.P },
            { Keys.Q, ImGuiKey.Q }, { Keys.R, ImGuiKey.R }, { Keys.S, ImGuiKey.S }, { Keys.T, ImGuiKey.T },
            { Keys.U, ImGuiKey.U }, { Keys.V, ImGuiKey.V }, { Keys.W, ImGuiKey.W }, { Keys.X, ImGuiKey.X },
            { Keys.Y, ImGuiKey.Y }, { Keys.Z, ImGuiKey.Z },
            { Keys.D0, ImGuiKey._0 }, { Keys.D1, ImGuiKey._1 }, { Keys.D2, ImGuiKey._2 }, { Keys.D3, ImGuiKey._3 },
            { Keys.D4, ImGuiKey._4 }, { Keys.D5, ImGuiKey._5 }, { Keys.D6, ImGuiKey._6 }, { Keys.D7, ImGuiKey._7 },
            { Keys.D8, ImGuiKey._8 }, { Keys.D9, ImGuiKey._9 },
            { Keys.LeftShift, ImGuiKey.LeftShift }, { Keys.RightShift, ImGuiKey.RightShift },
            { Keys.LeftControl, ImGuiKey.LeftCtrl }, { Keys.RightControl, ImGuiKey.RightCtrl },
            { Keys.LeftAlt, ImGuiKey.LeftAlt }, { Keys.RightAlt, ImGuiKey.RightAlt },
        };
    }
}