using OpenTK.Graphics.OpenGL4;
using OpenTK.Mathematics;

namespace Cherris.OpenTK;

internal struct DebugVertex
{
    public Vector3 Position;
    public Vector3 Color;
    public static readonly int Size = Vector3.SizeInBytes * 2;
}

internal class OpenGLDebugRenderer : IDisposable
{
    private readonly ShaderProgram _shader;
    private readonly int _viewLocation, _projectionLocation;
    private readonly List<DebugVertex> _vertices = new();
    private readonly int _vao;
    private readonly int _vbo;

    public OpenGLDebugRenderer()
    {
        const string vertSource = @"
#version 330 core
layout (location = 0) in vec3 aPosition;
layout (location = 1) in vec3 aColor;

uniform mat4 view;
uniform mat4 projection;

out vec3 f_color;

void main()
{
    gl_Position = projection * view * vec4(aPosition, 1.0);
    f_color = aColor;
}";
        const string fragSource = @"
#version 330 core
in vec3 f_color;
out vec4 FragColor;

void main()
{
    FragColor = vec4(f_color, 1.0);
}";

        _shader = new ShaderProgram(vertSource, fragSource);
        _viewLocation = _shader.GetUniformLocation("view");
        _projectionLocation = _shader.GetUniformLocation("projection");

        _vao = GL.GenVertexArray();
        _vbo = GL.GenBuffer();

        GL.BindVertexArray(_vao);
        GL.BindBuffer(BufferTarget.ArrayBuffer, _vbo);
        // Position
        GL.EnableVertexAttribArray(0);
        GL.VertexAttribPointer(0, 3, VertexAttribPointerType.Float, false, DebugVertex.Size, 0);
        // Color
        GL.EnableVertexAttribArray(1);
        GL.VertexAttribPointer(1, 3, VertexAttribPointerType.Float, false, DebugVertex.Size, Vector3.SizeInBytes);
        GL.BindVertexArray(0);
    }

    public void AddLine(System.Numerics.Vector3 start, System.Numerics.Vector3 end, System.Numerics.Vector3 color)
    {
        _vertices.Add(new DebugVertex { Position = ToOpenTKVector(start), Color = ToOpenTKVector(color) });
        _vertices.Add(new DebugVertex { Position = ToOpenTKVector(end), Color = ToOpenTKVector(color) });
    }

    public void AddBox(System.Numerics.Vector3 center, System.Numerics.Quaternion orientation, System.Numerics.Vector3 size, System.Numerics.Vector3 color)
    {
        Vector3 halfSize = ToOpenTKVector(size * 0.5f);
        Vector3[] corners = {
            new Vector3(-halfSize.X, -halfSize.Y, -halfSize.Z),
            new Vector3( halfSize.X, -halfSize.Y, -halfSize.Z),
            new Vector3( halfSize.X,  halfSize.Y, -halfSize.Z),
            new Vector3(-halfSize.X,  halfSize.Y, -halfSize.Z),
            new Vector3(-halfSize.X, -halfSize.Y,  halfSize.Z),
            new Vector3( halfSize.X, -halfSize.Y,  halfSize.Z),
            new Vector3( halfSize.X,  halfSize.Y,  halfSize.Z),
            new Vector3(-halfSize.X,  halfSize.Y,  halfSize.Z)
        };

        var otkOrientation = new Quaternion(orientation.X, orientation.Y, orientation.Z, orientation.W);
        var otkCenter = ToOpenTKVector(center);

        for (int i = 0; i < 8; i++)
        {
            corners[i] = Vector3.Transform(corners[i], otkOrientation) + otkCenter;
        }

        // Connect the corners
        int[] indices = {
            0, 1, 1, 2, 2, 3, 3, 0, // Bottom face
            4, 5, 5, 6, 6, 7, 7, 4, // Top face
            0, 4, 1, 5, 2, 6, 3, 7  // Connecting lines
        };

        var tkColor = ToOpenTKVector(color);
        for (int i = 0; i < indices.Length; i += 2)
        {
            _vertices.Add(new DebugVertex { Position = corners[indices[i]], Color = tkColor });
            _vertices.Add(new DebugVertex { Position = corners[indices[i + 1]], Color = tkColor });
        }
    }

    public void AddSphere(System.Numerics.Vector3 center, float radius, System.Numerics.Vector3 color)
    {
        const int segments = 16;
        var tkColor = ToOpenTKVector(color);
        var tkCenter = ToOpenTKVector(center);

        // Draw three circles on XY, XZ, YZ planes
        for (int axis = 0; axis < 3; axis++)
        {
            for (int i = 0; i < segments; i++)
            {
                float angle1 = (i / (float)segments) * 2.0f * MathF.PI;
                float angle2 = ((i + 1) / (float)segments) * 2.0f * MathF.PI;

                Vector3 p1 = Vector3.Zero;
                Vector3 p2 = Vector3.Zero;

                if (axis == 0) // XY plane
                {
                    p1 = new Vector3(MathF.Cos(angle1) * radius, MathF.Sin(angle1) * radius, 0);
                    p2 = new Vector3(MathF.Cos(angle2) * radius, MathF.Sin(angle2) * radius, 0);
                }
                else if (axis == 1) // XZ plane
                {
                    p1 = new Vector3(MathF.Cos(angle1) * radius, 0, MathF.Sin(angle1) * radius);
                    p2 = new Vector3(MathF.Cos(angle2) * radius, 0, MathF.Sin(angle2) * radius);
                }
                else // YZ plane
                {
                    p1 = new Vector3(0, MathF.Cos(angle1) * radius, MathF.Sin(angle1) * radius);
                    p2 = new Vector3(0, MathF.Cos(angle2) * radius, MathF.Sin(angle2) * radius);
                }

                _vertices.Add(new DebugVertex { Position = p1 + tkCenter, Color = tkColor });
                _vertices.Add(new DebugVertex { Position = p2 + tkCenter, Color = tkColor });
            }
        }
    }

    public void Clear()
    {
        _vertices.Clear();
    }

    public void Render(Matrix4 view, Matrix4 projection)
    {
        if (_vertices.Count == 0) return;

        _shader.Use();
        GL.UniformMatrix4(_viewLocation, false, ref view);
        GL.UniformMatrix4(_projectionLocation, false, ref projection);

        GL.BindVertexArray(_vao);
        GL.BindBuffer(BufferTarget.ArrayBuffer, _vbo);
        GL.BufferData(BufferTarget.ArrayBuffer, _vertices.Count * DebugVertex.Size, _vertices.ToArray(), BufferUsageHint.DynamicDraw);

        GL.Disable(EnableCap.DepthTest);
        GL.DrawArrays(PrimitiveType.Lines, 0, _vertices.Count);
        GL.Enable(EnableCap.DepthTest);

        GL.BindVertexArray(0);
        GL.BindBuffer(BufferTarget.ArrayBuffer, 0);
    }

    private static Vector3 ToOpenTKVector(System.Numerics.Vector3 v)
    {
        return new Vector3(v.X, v.Y, v.Z);
    }

    public void Dispose()
    {
        _shader?.Dispose();
        GL.DeleteVertexArray(_vao);
        GL.DeleteBuffer(_vbo);
    }
}