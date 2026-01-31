using OpenTK.Graphics.OpenGL4;
using OpenTK.Mathematics;

namespace Cherris.Rendering.OpenTK;

internal class OpenGLGridRenderer : IDisposable
{
    private readonly ShaderProgram _shader;
    private readonly int _viewLocation, _projectionLocation, _cameraPosLocation;
    private readonly int _vao;
    private readonly int _vbo;
    private readonly int _vertexCount;
    private bool _disposed;

    public OpenGLGridRenderer(float planeSize = 2000f)
    {
        _shader = ShaderProgram.FromFiles("Shaders/grid.vert", "Shaders/grid.frag");
        _viewLocation = _shader.GetUniformLocation("view");
        _projectionLocation = _shader.GetUniformLocation("projection");
        _cameraPosLocation = _shader.GetUniformLocation("uCameraPos");

        Vector3[] vertices =
        {
            new(-planeSize, 0, -planeSize),
            new( planeSize, 0, -planeSize),
            new(-planeSize, 0,  planeSize),
            new( planeSize, 0,  planeSize)
        };
        _vertexCount = vertices.Length;

        _vao = GL.GenVertexArray();
        _vbo = GL.GenBuffer();

        GL.BindVertexArray(_vao);
        GL.BindBuffer(BufferTarget.ArrayBuffer, _vbo);
        GL.BufferData(BufferTarget.ArrayBuffer, _vertexCount * Vector3.SizeInBytes, vertices, BufferUsageHint.StaticDraw);

        GL.EnableVertexAttribArray(0);
        GL.VertexAttribPointer(0, 3, VertexAttribPointerType.Float, false, Vector3.SizeInBytes, 0);
        GL.BindVertexArray(0);
    }

    public void Render(Matrix4 view, Matrix4 projection, Vector3 cameraPos)
    {
        if (_vertexCount == 0)
        {
            return;
        }

        GL.Enable(EnableCap.Blend);
        GL.BlendFunc(BlendingFactor.SrcAlpha, BlendingFactor.OneMinusSrcAlpha);
        GL.Disable(EnableCap.CullFace);
        GL.DepthMask(false);

        _shader.Use();
        GL.UniformMatrix4(_viewLocation, false, ref view);
        GL.UniformMatrix4(_projectionLocation, false, ref projection);
        GL.Uniform3(_cameraPosLocation, cameraPos);

        GL.BindVertexArray(_vao);
        GL.DrawArrays(PrimitiveType.TriangleStrip, 0, _vertexCount);
        GL.BindVertexArray(0);

        GL.DepthMask(true);
        GL.Enable(EnableCap.CullFace);
        GL.Disable(EnableCap.Blend);
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _shader?.Dispose();
        GL.DeleteVertexArray(_vao);
        GL.DeleteBuffer(_vbo);
        _disposed = true;
    }
}