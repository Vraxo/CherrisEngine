using OpenTK.Graphics.OpenGL4;
using OpenTK.Mathematics;
using System;
using System.Collections.Generic;
using System.Diagnostics;

namespace Cherris;

internal class OpenGLSceneRenderer : IDisposable
{
    private readonly ShaderProgram _shaderProgram;
    private readonly int _mvpLocation;
    private readonly int _textureLocation;
    private readonly int _tilingLocation;
    private readonly int _emissiveLocation;

    public OpenGLSceneRenderer()
    {
        const string vertSource = @"
#version 330 core
layout (location = 0) in vec3 aPosition;
layout (location = 1) in vec4 aColor;
layout (location = 2) in vec2 aTexCoord;

uniform mat4 mvp;
uniform vec2 uTiling;

out vec4 fsin_Color;
out vec2 fsin_TexCoord;

void main()
{
    gl_Position = mvp * vec4(aPosition, 1.0);
    fsin_Color = aColor;
    fsin_TexCoord = aTexCoord * uTiling;
}";

        const string fragSource = @"
#version 330 core
in vec4 fsin_Color;
in vec2 fsin_TexCoord;

uniform sampler2D uTexture;
uniform vec3 uEmissive;

out vec4 FragColor;

void main()
{
    vec4 texColor = texture(uTexture, fsin_TexCoord);
    vec3 finalColor = (texColor.rgb * fsin_Color.rgb) + uEmissive;
    FragColor = vec4(finalColor, texColor.a * fsin_Color.a);
}";

        _shaderProgram = new ShaderProgram(vertSource, fragSource);
        _mvpLocation = _shaderProgram.GetUniformLocation("mvp");
        _textureLocation = _shaderProgram.GetUniformLocation("uTexture");
        _tilingLocation = _shaderProgram.GetUniformLocation("uTiling");
        _emissiveLocation = _shaderProgram.GetUniformLocation("uEmissive");
    }

    public void Render(IEnumerable<GameObject> gameObjects, Matrix4 view, Matrix4 projection)
    {
        _shaderProgram.Use();
        GL.Uniform1(_textureLocation, 0);
        foreach (var go in gameObjects)
        {
            var mr = go.GetComponent<MeshRenderer>();
            if (mr?.Mesh is null || go.GetComponent<Skybox>() != null) continue;
            var data = GetOrCreateBackendData(mr);
            DrawObject(go, mr, data, view, projection);
        }
    }

    private void DrawObject(GameObject go, MeshRenderer meshRenderer, OpenGLMeshRendererData data, Matrix4 view, Matrix4 projection)
    {
        if (meshRenderer.Texture is OpenTKTexture glTexture)
        {
            glTexture.Bind(TextureUnit.Texture0);
        }
        else
        {
            GL.BindTexture(TextureTarget.Texture2D, 0);
        }

        var model = ToOpenTKMatrix(go.Transform.GetModelMatrix());
        var mvp = model * view * projection;

        GL.UniformMatrix4(_mvpLocation, false, ref mvp);
        GL.Uniform2(_tilingLocation, meshRenderer.TextureTiling.X, meshRenderer.TextureTiling.Y);
        GL.Uniform3(_emissiveLocation, meshRenderer.EmissiveColor.X, meshRenderer.EmissiveColor.Y, meshRenderer.EmissiveColor.Z);

        GL.BindVertexArray(data.VaoHandle);
        GL.DrawElements(PrimitiveType.Triangles, data.IndexCount, DrawElementsType.UnsignedShort, 0);
        GL.BindVertexArray(0);

        CheckGLError($"Draw '{go.Name}'");
    }

    private OpenGLMeshRendererData GetOrCreateBackendData(MeshRenderer mr)
    {
        if (mr.BackendData is OpenGLMeshRendererData d) return d;
        var nd = new OpenGLMeshRendererData(mr.Mesh);
        mr.BackendData = nd;
        return nd;
    }

    private static Matrix4 ToOpenTKMatrix(System.Numerics.Matrix4x4 m)
    {
        return new Matrix4(
            m.M11, m.M12, m.M13, m.M14,
            m.M21, m.M22, m.M23, m.M24,
            m.M31, m.M32, m.M33, m.M34,
            m.M41, m.M42, m.M43, m.M44
        );
    }

    [Conditional("DEBUG")]
    private static void CheckGLError(string context)
    {
        var error = GL.GetError();
        while (error != ErrorCode.NoError)
        {
            Console.WriteLine($"[OpenGL Error] After {context}: {error}");
            error = GL.GetError();
        }
    }

    public void Dispose()
    {
        _shaderProgram?.Dispose();
    }
}