using OpenTK.Graphics.OpenGL4;
using OpenTK.Mathematics;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Numerics;

namespace Cherris;

internal class OpenGLSceneRenderer : IDisposable
{
    private readonly ShaderProgram _shaderProgram;
    private readonly int _modelLocation, _viewLocation, _projectionLocation;
    private readonly int _textureLocation, _tilingLocation, _emissiveLocation;
    private readonly int _viewPosLocation;
    private readonly int _hasLightLocation;
    private readonly int _lightDirLocation, _lightColorLocation, _lightIntensityLocation, _lightAmbientStrengthLocation;
    private readonly int _specularIntensityLocation, _shininessLocation;


    public OpenGLSceneRenderer()
    {
        const string vertSource = @"
#version 330 core
layout (location = 0) in vec3 aPosition;
layout (location = 1) in vec3 aNormal;
layout (location = 2) in vec4 aColor;
layout (location = 3) in vec2 aTexCoord;

uniform mat4 model;
uniform mat4 view;
uniform mat4 projection;
uniform vec2 uTiling;

out vec3 FragPos;
out vec3 Normal;
out vec4 fsin_Color;
out vec2 fsin_TexCoord;

void main()
{
    FragPos = vec3(model * vec4(aPosition, 1.0));
    Normal = mat3(transpose(inverse(model))) * aNormal;
    
    gl_Position = projection * view * vec4(FragPos, 1.0);
    fsin_Color = aColor;
    fsin_TexCoord = aTexCoord * uTiling;
}";

        const string fragSource = @"
#version 330 core
in vec4 fsin_Color;
in vec2 fsin_TexCoord;
in vec3 FragPos;
in vec3 Normal;

uniform sampler2D uTexture;
uniform vec3 uEmissive;
uniform vec3 uViewPos;

// Material
uniform float uSpecularIntensity;
uniform float uShininess;

// Light
uniform bool uHasLight;
uniform vec3 uLightDir;
uniform vec3 uLightColor;
uniform float uLightIntensity;
uniform float uLightAmbientStrength;


out vec4 FragColor;

void main()
{
    vec3 albedo = texture(uTexture, fsin_TexCoord).rgb * fsin_Color.rgb;
    vec3 norm = normalize(Normal);
    vec3 finalColor;

    if(uHasLight) {
        // Ambient
        vec3 ambient = uLightAmbientStrength * uLightColor * uLightIntensity;

        // Diffuse
        vec3 lightDir = normalize(uLightDir);
        float diff = max(dot(norm, lightDir), 0.0);
        vec3 diffuse = diff * uLightColor * uLightIntensity;

        // Specular (Blinn-Phong)
        vec3 viewDir = normalize(uViewPos - FragPos);
        vec3 halfwayDir = normalize(lightDir + viewDir);
        float spec = pow(max(dot(norm, halfwayDir), 0.0), uShininess);
        vec3 specular = uSpecularIntensity * spec * uLightColor * uLightIntensity;

        finalColor = (ambient + diffuse) * albedo + specular;
    } else {
        // No light in scene, just use albedo
        finalColor = albedo;
    }
    
    finalColor += uEmissive;

    FragColor = vec4(finalColor, texture(uTexture, fsin_TexCoord).a * fsin_Color.a);
}";

        _shaderProgram = new ShaderProgram(vertSource, fragSource);
        _modelLocation = _shaderProgram.GetUniformLocation("model");
        _viewLocation = _shaderProgram.GetUniformLocation("view");
        _projectionLocation = _shaderProgram.GetUniformLocation("projection");

        _textureLocation = _shaderProgram.GetUniformLocation("uTexture");
        _tilingLocation = _shaderProgram.GetUniformLocation("uTiling");
        _emissiveLocation = _shaderProgram.GetUniformLocation("uEmissive");

        _viewPosLocation = _shaderProgram.GetUniformLocation("uViewPos");

        _hasLightLocation = _shaderProgram.GetUniformLocation("uHasLight");
        _lightDirLocation = _shaderProgram.GetUniformLocation("uLightDir");
        _lightColorLocation = _shaderProgram.GetUniformLocation("uLightColor");
        _lightIntensityLocation = _shaderProgram.GetUniformLocation("uLightIntensity");
        _lightAmbientStrengthLocation = _shaderProgram.GetUniformLocation("uLightAmbientStrength");

        _specularIntensityLocation = _shaderProgram.GetUniformLocation("uSpecularIntensity");
        _shininessLocation = _shaderProgram.GetUniformLocation("uShininess");
    }

    public void Render(IEnumerable<GameObject> gameObjects, Matrix4 view, Matrix4 projection)
    {
        _shaderProgram.Use();

        // Set uniforms that are constant for the frame
        GL.Uniform1(_textureLocation, 0);
        GL.UniformMatrix4(_viewLocation, false, ref view);
        GL.UniformMatrix4(_projectionLocation, false, ref projection);
        var viewPos = view.Inverted().Row3.Xyz;
        GL.Uniform3(_viewPosLocation, viewPos.X, viewPos.Y, viewPos.Z);

        // Find and set light uniforms
        var directionalLight = gameObjects
            .Select(g => g.GetComponent<Light>())
            .FirstOrDefault(l => l is not null && l.Type == LightType.Directional);

        if (directionalLight != null)
        {
            GL.Uniform1(_hasLightLocation, 1);
            // The light's forward vector is the direction it's traveling.
            // For lighting calculations, we need the vector FROM the surface TO the light, which is the inverse.
            var lightTravelDirection = System.Numerics.Vector3.Normalize(System.Numerics.Vector3.Transform(-System.Numerics.Vector3.UnitZ, directionalLight.GameObject.Transform.Rotation));
            var directionToLight = -lightTravelDirection;
            GL.Uniform3(_lightDirLocation, directionToLight.X, directionToLight.Y, directionToLight.Z);
            GL.Uniform3(_lightColorLocation, directionalLight.Color.X, directionalLight.Color.Y, directionalLight.Color.Z);
            GL.Uniform1(_lightIntensityLocation, directionalLight.Intensity);
            GL.Uniform1(_lightAmbientStrengthLocation, directionalLight.AmbientStrength);
        }
        else
        {
            GL.Uniform1(_hasLightLocation, 0);
        }

        // Render each object
        foreach (var go in gameObjects)
        {
            var mr = go.GetComponent<MeshRenderer>();
            if (mr?.Mesh is null || go.GetComponent<Skybox>() is not null) continue;
            var data = GetOrCreateBackendData(mr);
            DrawObject(go, mr, data);
        }
    }

    private void DrawObject(GameObject go, MeshRenderer meshRenderer, OpenGLMeshRendererData data)
    {
        var material = meshRenderer.Material;
        if (material.Texture is OpenTKTexture glTexture)
        {
            glTexture.Bind(TextureUnit.Texture0);
        }
        else
        {
            GL.BindTexture(TextureTarget.Texture2D, 0);
        }

        var model = ToOpenTKMatrix(go.Transform.GetModelMatrix());

        GL.UniformMatrix4(_modelLocation, false, ref model);
        GL.Uniform2(_tilingLocation, material.TextureTiling.X, material.TextureTiling.Y);
        GL.Uniform3(_emissiveLocation, material.EmissiveColor.X, material.EmissiveColor.Y, material.EmissiveColor.Z);
        GL.Uniform1(_specularIntensityLocation, material.SpecularIntensity);
        GL.Uniform1(_shininessLocation, material.Shininess);

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