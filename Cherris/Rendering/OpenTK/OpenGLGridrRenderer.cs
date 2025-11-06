using Cherris.Components;
using Cherris.Core;
using OpenTK.Graphics.OpenGL4;
using OpenTK.Mathematics;
using System;
using System.Collections.Generic;
using System.Diagnostics;

namespace Cherris.OpenTK;

internal struct GridVertex
{
    public Vector3 Position;
    public Vector3 Color;
    public static readonly int Size = Vector3.SizeInBytes * 2;
}

internal class OpenGLGridRenderer : IDisposable
{
    private readonly ShaderProgram _shader;
    private readonly int _viewLocation, _projectionLocation;
    private readonly int _vao;
    private readonly int _vbo;
    private readonly int _vertexCount;
    private bool _disposed;

    public OpenGLGridRenderer(int size = 20, float step = 1.0f)
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

        var vertices = new List<GridVertex>();
        var gridColor = new Vector3(0.3f, 0.3f, 0.3f);
        var axisColorX = new Vector3(0.8f, 0.2f, 0.2f);
        var axisColorZ = new Vector3(0.2f, 0.3f, 0.8f);

        for (float i = -size; i <= size; i += step)
        {
            // Lines parallel to Z axis (along X)
            vertices.Add(new GridVertex { Position = new Vector3(i, 0, -size), Color = (i == 0) ? axisColorZ : gridColor });
            vertices.Add(new GridVertex { Position = new Vector3(i, 0, size), Color = (i == 0) ? axisColorZ : gridColor });

            // Lines parallel to X axis (along Z)
            vertices.Add(new GridVertex { Position = new Vector3(-size, 0, i), Color = (i == 0) ? axisColorX : gridColor });
            vertices.Add(new GridVertex { Position = new Vector3(size, 0, i), Color = (i == 0) ? axisColorX : gridColor });
        }
        _vertexCount = vertices.Count;

        _vao = GL.GenVertexArray();
        _vbo = GL.GenBuffer();

        GL.BindVertexArray(_vao);
        GL.BindBuffer(BufferTarget.ArrayBuffer, _vbo);
        GL.BufferData(BufferTarget.ArrayBuffer, _vertexCount * GridVertex.Size, vertices.ToArray(), BufferUsageHint.StaticDraw);

        // Position attribute
        GL.EnableVertexAttribArray(0);
        GL.VertexAttribPointer(0, 3, VertexAttribPointerType.Float, false, GridVertex.Size, 0);
        // Color attribute
        GL.EnableVertexAttribArray(1);
        GL.VertexAttribPointer(1, 3, VertexAttribPointerType.Float, false, GridVertex.Size, Vector3.SizeInBytes);
        GL.BindVertexArray(0);
    }

    public void Render(Matrix4 view, Matrix4 projection)
    {
        if (_vertexCount == 0) return;

        _shader.Use();
        GL.UniformMatrix4(_viewLocation, false, ref view);
        GL.UniformMatrix4(_projectionLocation, false, ref projection);

        GL.BindVertexArray(_vao);
        GL.DrawArrays(PrimitiveType.Lines, 0, _vertexCount);
        GL.BindVertexArray(0);
    }

    public void Dispose()
    {
        if (!_disposed)
        {
            _shader?.Dispose();
            GL.DeleteVertexArray(_vao);
            GL.DeleteBuffer(_vbo);
            _disposed = true;
        }
    }
}