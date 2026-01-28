using OpenTK.Graphics.OpenGL4;
using OpenTK.Mathematics;
using System;

namespace Cherris.OpenTK;

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
        const string vertSource = @"
#version 330 core
layout (location = 0) in vec3 aPosition; // World space position of quad vertex

uniform mat4 view;
uniform mat4 projection;
uniform vec3 uCameraPos;

out vec3 vWorldPos;
out vec3 vToCamera;

void main()
{
    vWorldPos = aPosition;
    vToCamera = uCameraPos - vWorldPos;
    gl_Position = projection * view * vec4(aPosition, 1.0);
}";
        const string fragSource = @"
#version 330 core
in vec3 vWorldPos;
in vec3 vToCamera;

out vec4 FragColor;

void main()
{
    float dist = length(vToCamera);
    
    // Choose grid spacing based on distance from camera
    float spacing = 1.0;
    if (dist > 200.0) spacing = 10.0;
    else if (dist > 50.0) spacing = 5.0;

    vec2 coord = vWorldPos.xz / spacing;
    
    // Compute anti-aliased grid lines
    vec2 grid = abs(fract(coord - 0.5) - 0.5) / fwidth(coord);
    float line = min(grid.x, grid.y);
    
    // Major lines every 10 minor lines
    vec2 coord_major = vWorldPos.xz / (spacing * 10.0);
    vec2 grid_major = abs(fract(coord_major - 0.5) - 0.5) / fwidth(coord_major);
    float line_major = min(grid_major.x, grid_major.y);
    
    // Combine to get grid alpha
    float grid_alpha = 1.0 - min(line, 1.0);
    float grid_major_alpha = 1.0 - min(line_major, 1.0);
    
    // Axes
    float axis_width = 1.5; // in pixels
    vec2 axis_d = fwidth(vWorldPos.xz);
    float x_axis_alpha = smoothstep(axis_width * axis_d.y, 0.0, abs(vWorldPos.z)); // Line on Z=0 is X axis
    float z_axis_alpha = smoothstep(axis_width * axis_d.x, 0.0, abs(vWorldPos.x)); // Line on X=0 is Z axis
    float axes_alpha = max(x_axis_alpha, z_axis_alpha);
    
    // Fade out in the distance
    float fade_start = 250.0;
    float fade_end = 350.0;
    float fade = 1.0 - smoothstep(fade_start, fade_end, dist);
    
    float final_alpha = (max(grid_alpha, axes_alpha) + grid_major_alpha) * fade;
    
    if (final_alpha < 0.01)
        discard;
    
    // Determine color
    vec3 color = vec3(0.3); // Minor grid line color
    color = mix(color, vec3(0.4), grid_major_alpha); // Major grid line color
    color = mix(color, vec3(0.8, 0.2, 0.2), x_axis_alpha); // X-Axis (line at Z=0) -> Red
    color = mix(color, vec3(0.2, 0.3, 0.8), z_axis_alpha); // Z-Axis (line at X=0) -> Blue

    FragColor = vec4(color, final_alpha);
}";

        _shader = new ShaderProgram(vertSource, fragSource);
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

        // Position attribute
        GL.EnableVertexAttribArray(0);
        GL.VertexAttribPointer(0, 3, VertexAttribPointerType.Float, false, Vector3.SizeInBytes, 0);
        GL.BindVertexArray(0);
    }

    public void Render(Matrix4 view, Matrix4 projection, Vector3 cameraPos)
    {
        if (_vertexCount == 0) return;

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
        if (!_disposed)
        {
            _shader?.Dispose();
            GL.DeleteVertexArray(_vao);
            GL.DeleteBuffer(_vbo);
            _disposed = true;
        }
    }
}