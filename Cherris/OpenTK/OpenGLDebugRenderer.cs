using OpenTK.Graphics.OpenGL4;
using OpenTK.Mathematics;
using System;
using System.Collections.Generic;

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