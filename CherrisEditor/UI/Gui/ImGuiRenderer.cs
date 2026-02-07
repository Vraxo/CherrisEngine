using ImGuiNET;
using OpenTK.Graphics.OpenGL4;
using OpenTK.Mathematics;
using System.Runtime.CompilerServices;

namespace CherrisEditor.UI.Gui;

internal sealed class ImGuiRenderer : IDisposable
{
    private int _vertexArray;
    private int _vertexBuffer;
    private int _indexBuffer;
    private int _shader;
    private int _projectionUniformLocation;
    private int _fontTextureUniformLocation;

    private readonly int _vertexBufferSize;
    private readonly int _indexBufferSize;

    private readonly int _windowWidth;
    private readonly int _windowHeight;

    public ImGuiRenderer(int windowWidth, int windowHeight)
    {
        _windowWidth = windowWidth;
        _windowHeight = windowHeight;

        const int initialVertexBufferSize = 10000;
        const int initialIndexBufferSize = 2000;

        _vertexBufferSize = initialVertexBufferSize;
        _indexBufferSize = initialIndexBufferSize;

        CreateVertexArray();
        CreateBuffers();
        CreateShader();
    }

    private void CreateVertexArray()
    {
        _vertexArray = GL.GenVertexArray();
        GL.BindVertexArray(_vertexArray);

        int stride = Unsafe.SizeOf<ImDrawVert>();

        GL.EnableVertexAttribArray(0);
        GL.VertexAttribPointer(0, 2, VertexAttribPointerType.Float, false, stride, 0);

        GL.EnableVertexAttribArray(1);
        GL.VertexAttribPointer(1, 2, VertexAttribPointerType.Float, false, stride, 8);

        GL.EnableVertexAttribArray(2);
        GL.VertexAttribPointer(2, 4, VertexAttribPointerType.UnsignedByte, true, stride, 16);

        GL.BindVertexArray(0);
    }

    private void CreateBuffers()
    {
        _vertexBuffer = GL.GenBuffer();
        GL.BindBuffer(BufferTarget.ArrayBuffer, _vertexBuffer);
        GL.BufferData(BufferTarget.ArrayBuffer, _vertexBufferSize, IntPtr.Zero, BufferUsageHint.DynamicDraw);

        _indexBuffer = GL.GenBuffer();
        GL.BindBuffer(BufferTarget.ElementArrayBuffer, _indexBuffer);
        GL.BufferData(BufferTarget.ElementArrayBuffer, _indexBufferSize, IntPtr.Zero, BufferUsageHint.DynamicDraw);
    }

    private void CreateShader()
    {
        const string vertexSource = @"#version 330 core
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

        const string fragmentSource = @"#version 330 core
uniform sampler2D in_fontTexture;
in vec4 color;
in vec2 texCoord;
out vec4 outputColor;
void main()
{
    outputColor = color * texture(in_fontTexture, texCoord);
}";

        int vertexShader = CompileShader(ShaderType.VertexShader, vertexSource);
        int fragmentShader = CompileShader(ShaderType.FragmentShader, fragmentSource);

        _shader = GL.CreateProgram();
        GL.AttachShader(_shader, vertexShader);
        GL.AttachShader(_shader, fragmentShader);
        GL.LinkProgram(_shader);

        GL.DetachShader(_shader, vertexShader);
        GL.DetachShader(_shader, fragmentShader);
        GL.DeleteShader(vertexShader);
        GL.DeleteShader(fragmentShader);

        _projectionUniformLocation = GL.GetUniformLocation(_shader, "projection_matrix");
        _fontTextureUniformLocation = GL.GetUniformLocation(_shader, "in_fontTexture");
    }

    private static int CompileShader(ShaderType type, string source)
    {
        int shader = GL.CreateShader(type);
        GL.ShaderSource(shader, source);
        GL.CompileShader(shader);
        return shader;
    }

    public unsafe void Render(ImDrawDataPtr drawData)
    {
        if (drawData.CmdListsCount == 0)
        {
            return;
        }

        SaveGLState(out int lastProgram, out int lastArrayBuffer, out int lastVertexArray, out int lastActiveTexture);

        SetupRenderState();
        drawData.ScaleClipRects(ImGui.GetIO().DisplayFramebufferScale);

        // Cast CmdLists to a pointer to pointer array for safe access.
        ImDrawListPtr* cmdLists = (ImDrawListPtr*)drawData.CmdLists;

        for (int i = 0; i < drawData.CmdListsCount; i++)
        {
            RenderCommandList(cmdLists[i]);
        }

        RestoreGLState(lastProgram, lastArrayBuffer, lastVertexArray, lastActiveTexture);
    }

    private void SetupRenderState()
    {
        GL.Enable(EnableCap.Blend);
        GL.BlendEquation(BlendEquationMode.FuncAdd);
        GL.BlendFunc(BlendingFactor.SrcAlpha, BlendingFactor.OneMinusSrcAlpha);
        GL.Disable(EnableCap.CullFace);
        GL.Disable(EnableCap.DepthTest);
        GL.Enable(EnableCap.ScissorTest);

        GL.BindVertexArray(_vertexArray);

        Matrix4 projection = Matrix4.CreateOrthographicOffCenter(
            0.0f, _windowWidth,
            _windowHeight, 0.0f,
            -1.0f, 1.0f);

        GL.UseProgram(_shader);
        GL.UniformMatrix4(_projectionUniformLocation, false, ref projection);
        GL.Uniform1(_fontTextureUniformLocation, 0);
    }

    private unsafe void RenderCommandList(ImDrawListPtr cmdList)
    {
        UploadBuffers(cmdList);

        // Cast Data to byte* to perform manual pointer arithmetic.
        // This avoids the CS0021 error caused by attempting to use [] on nint or ambiguous struct pointers.
        byte* bufferPtr = (byte*)cmdList.CmdBuffer.Data;
        int cmdSize = Unsafe.SizeOf<ImDrawCmd>();

        for (int i = 0; i < cmdList.CmdBuffer.Size; i++)
        {
            // Calculate the offset and read the struct directly using Unsafe.AsRef.
            // This bypasses the need for an indexer operator on a pointer type.
            ImDrawCmd cmd = Unsafe.AsRef<ImDrawCmd>(bufferPtr + (i * cmdSize));

            if (cmd.UserCallback != null)
            {
                continue;
            }

            GL.ActiveTexture(TextureUnit.Texture0);
            GL.BindTexture(TextureTarget.Texture2D, (int)cmd.TextureId);

            Vector4 clip = new(cmd.ClipRect.X, cmd.ClipRect.Y, cmd.ClipRect.Z, cmd.ClipRect.W);
            GL.Scissor((int)clip.X, _windowHeight - (int)clip.W, (int)(clip.Z - clip.X), (int)(clip.W - clip.Y));

            GL.DrawElementsBaseVertex(
                PrimitiveType.Triangles,
                (int)cmd.ElemCount,
                DrawElementsType.UnsignedShort,
                (IntPtr)(cmd.IdxOffset * sizeof(ushort)),
                (int)cmd.VtxOffset);
        }
    }

    private unsafe void UploadBuffers(ImDrawListPtr cmdList)
    {
        GL.BindBuffer(BufferTarget.ArrayBuffer, _vertexBuffer);
        GL.BufferData(
            BufferTarget.ArrayBuffer,
            cmdList.VtxBuffer.Size * Unsafe.SizeOf<ImDrawVert>(),
            cmdList.VtxBuffer.Data,
            BufferUsageHint.StreamDraw);

        GL.BindBuffer(BufferTarget.ElementArrayBuffer, _indexBuffer);
        GL.BufferData(
            BufferTarget.ElementArrayBuffer,
            cmdList.IdxBuffer.Size * sizeof(ushort),
            cmdList.IdxBuffer.Data,
            BufferUsageHint.StreamDraw);
    }

    private static void SaveGLState(out int lastProgram, out int lastArrayBuffer, out int lastVertexArray, out int lastActiveTexture)
    {
        GL.GetInteger(GetPName.CurrentProgram, out lastProgram);
        GL.GetInteger(GetPName.ArrayBufferBinding, out lastArrayBuffer);
        GL.GetInteger(GetPName.VertexArrayBinding, out lastVertexArray);
        GL.GetInteger(GetPName.ActiveTexture, out lastActiveTexture);
    }

    private static void RestoreGLState(int lastProgram, int lastArrayBuffer, int lastVertexArray, int lastActiveTexture)
    {
        GL.UseProgram(lastProgram);
        GL.BindVertexArray(lastVertexArray);
        GL.BindBuffer(BufferTarget.ArrayBuffer, lastArrayBuffer);
        GL.ActiveTexture((TextureUnit)lastActiveTexture);
    }

    public void Dispose()
    {
        GL.DeleteProgram(_shader);
        GL.DeleteBuffer(_vertexBuffer);
        GL.DeleteBuffer(_indexBuffer);
        GL.DeleteVertexArray(_vertexArray);
    }
}