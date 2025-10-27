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
    private const int MAX_POINT_LIGHTS = 4;

    private readonly ShaderProgram _shaderProgram;
    private readonly int _modelLocation, _viewLocation, _projectionLocation;
    private readonly int _tilingLocation, _emissiveLocation, _viewPosLocation;

    // Material Uniform Locations
    private readonly int _materialTextureLocation, _materialShininessLocation, _materialSpecularIntensityLocation;

    // Directional Light Uniform Locations
    private readonly int _dirLightDirLocation, _dirLightColorLocation, _dirLightIntensityLocation, _dirLightAmbientStrengthLocation;
    private readonly int _hasDirLightLocation;

    // Point Light Uniform Locations
    private readonly int _numPointLightsLocation;
    private readonly int[] _pointLightPosLocations = new int[MAX_POINT_LIGHTS];
    private readonly int[] _pointLightColorLocations = new int[MAX_POINT_LIGHTS];
    private readonly int[] _pointLightIntensityLocations = new int[MAX_POINT_LIGHTS];
    private readonly int[] _pointLightConstantLocations = new int[MAX_POINT_LIGHTS];
    private readonly int[] _pointLightLinearLocations = new int[MAX_POINT_LIGHTS];
    private readonly int[] _pointLightQuadraticLocations = new int[MAX_POINT_LIGHTS];


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
#define MAX_POINT_LIGHTS 4

in vec4 fsin_Color;
in vec2 fsin_TexCoord;
in vec3 FragPos;
in vec3 Normal;

struct Material {
    sampler2D texture_diffuse;
    float shininess;
    float specularIntensity;
};

struct DirLight {
    vec3 direction;
    vec3 color;
    float intensity;
    float ambientStrength;
};

struct PointLight {
    vec3 position;
    vec3 color;
    float intensity;
    float constant;
    float linear;
    float quadratic;
};

out vec4 FragColor;

// --- Uniforms ---
uniform vec3 uViewPos;
uniform vec3 uEmissive;

uniform Material uMaterial;
uniform DirLight uDirLight;
uniform bool uHasDirLight;

uniform PointLight uPointLights[MAX_POINT_LIGHTS];
uniform int uNumPointLights;


// --- Function Declarations ---
vec3 CalcDirLight(DirLight light, vec3 normal, vec3 viewDir, vec3 albedo);
vec3 CalcPointLight(PointLight light, vec3 normal, vec3 fragPos, vec3 viewDir, vec3 albedo);

void main()
{
    vec3 albedo = texture(uMaterial.texture_diffuse, fsin_TexCoord).rgb * fsin_Color.rgb;
    vec3 norm = normalize(Normal);
    vec3 viewDir = normalize(uViewPos - FragPos);

    vec3 finalColor = vec3(0.0);

    if(uHasDirLight) {
        finalColor += CalcDirLight(uDirLight, norm, viewDir, albedo);
    }

    for(int i = 0; i < uNumPointLights; i++) {
        finalColor += CalcPointLight(uPointLights[i], norm, FragPos, viewDir, albedo);
    }

    // If no lights, the color would be black. Add albedo so unlit scenes are visible.
    if (!uHasDirLight && uNumPointLights == 0) {
        finalColor = albedo;
    }

    finalColor += uEmissive;

    FragColor = vec4(finalColor, texture(uMaterial.texture_diffuse, fsin_TexCoord).a * fsin_Color.a);
}

vec3 CalcDirLight(DirLight light, vec3 normal, vec3 viewDir, vec3 albedo) {
    vec3 lightDir = normalize(light.direction);
    
    // Ambient
    vec3 ambient = light.ambientStrength * light.color * light.intensity;

    // Diffuse
    float diff = max(dot(normal, lightDir), 0.0);
    vec3 diffuse = diff * light.color * light.intensity;

    // Specular
    vec3 halfwayDir = normalize(lightDir + viewDir);
    float spec = pow(max(dot(normal, halfwayDir), 0.0), uMaterial.shininess);
    vec3 specular = uMaterial.specularIntensity * spec * light.color * light.intensity;

    return (ambient + diffuse) * albedo + specular;
}

vec3 CalcPointLight(PointLight light, vec3 normal, vec3 fragPos, vec3 viewDir, vec3 albedo) {
    vec3 lightDir = normalize(light.position - fragPos);

    // Diffuse
    float diff = max(dot(normal, lightDir), 0.0);
    vec3 diffuse = diff * light.color * light.intensity;

    // Specular
    vec3 halfwayDir = normalize(lightDir + viewDir);
    float spec = pow(max(dot(normal, halfwayDir), 0.0), uMaterial.shininess);
    vec3 specular = uMaterial.specularIntensity * spec * light.color * light.intensity;

    // Attenuation
    float distance = length(light.position - fragPos);
    float attenuation = 1.0 / (light.constant + light.linear * distance + light.quadratic * (distance * distance));
    
    diffuse *= attenuation;
    specular *= attenuation;

    return diffuse * albedo + specular;
}";

        _shaderProgram = new ShaderProgram(vertSource, fragSource);

        // Standard uniforms
        _modelLocation = _shaderProgram.GetUniformLocation("model");
        _viewLocation = _shaderProgram.GetUniformLocation("view");
        _projectionLocation = _shaderProgram.GetUniformLocation("projection");
        _tilingLocation = _shaderProgram.GetUniformLocation("uTiling");
        _emissiveLocation = _shaderProgram.GetUniformLocation("uEmissive");
        _viewPosLocation = _shaderProgram.GetUniformLocation("uViewPos");

        // Material uniforms
        _materialTextureLocation = _shaderProgram.GetUniformLocation("uMaterial.texture_diffuse");
        _materialShininessLocation = _shaderProgram.GetUniformLocation("uMaterial.shininess");
        _materialSpecularIntensityLocation = _shaderProgram.GetUniformLocation("uMaterial.specularIntensity");

        // Directional light uniforms
        _hasDirLightLocation = _shaderProgram.GetUniformLocation("uHasDirLight");
        _dirLightDirLocation = _shaderProgram.GetUniformLocation("uDirLight.direction");
        _dirLightColorLocation = _shaderProgram.GetUniformLocation("uDirLight.color");
        _dirLightIntensityLocation = _shaderProgram.GetUniformLocation("uDirLight.intensity");
        _dirLightAmbientStrengthLocation = _shaderProgram.GetUniformLocation("uDirLight.ambientStrength");

        // Point light uniforms
        _numPointLightsLocation = _shaderProgram.GetUniformLocation("uNumPointLights");
        for (int i = 0; i < MAX_POINT_LIGHTS; i++)
        {
            _pointLightPosLocations[i] = _shaderProgram.GetUniformLocation($"uPointLights[{i}].position");
            _pointLightColorLocations[i] = _shaderProgram.GetUniformLocation($"uPointLights[{i}].color");
            _pointLightIntensityLocations[i] = _shaderProgram.GetUniformLocation($"uPointLights[{i}].intensity");
            _pointLightConstantLocations[i] = _shaderProgram.GetUniformLocation($"uPointLights[{i}].constant");
            _pointLightLinearLocations[i] = _shaderProgram.GetUniformLocation($"uPointLights[{i}].linear");
            _pointLightQuadraticLocations[i] = _shaderProgram.GetUniformLocation($"uPointLights[{i}].quadratic");
        }
    }

    public void Render(IEnumerable<GameObject> gameObjects, Matrix4 view, Matrix4 projection)
    {
        _shaderProgram.Use();

        // Set uniforms that are constant for the frame
        GL.Uniform1(_materialTextureLocation, 0);
        GL.UniformMatrix4(_viewLocation, false, ref view);
        GL.UniformMatrix4(_projectionLocation, false, ref projection);
        var viewPos = view.Inverted().Row3.Xyz;
        GL.Uniform3(_viewPosLocation, viewPos.X, viewPos.Y, viewPos.Z);

        // Find and set light uniforms
        var allLights = gameObjects.Select(g => g.GetComponent<Light>()).Where(l => l is not null).ToList();
        var directionalLight = allLights.FirstOrDefault(l => l.Type == LightType.Directional);
        var pointLights = allLights.Where(l => l.Type == LightType.Point).Take(MAX_POINT_LIGHTS).ToList();

        if (directionalLight != null)
        {
            GL.Uniform1(_hasDirLightLocation, 1);
            var lightTravelDirection = System.Numerics.Vector3.Normalize(System.Numerics.Vector3.Transform(-System.Numerics.Vector3.UnitZ, directionalLight.GameObject.Transform.Rotation));
            var directionToLight = -lightTravelDirection;
            GL.Uniform3(_dirLightDirLocation, directionToLight.X, directionToLight.Y, directionToLight.Z);
            GL.Uniform3(_dirLightColorLocation, directionalLight.Color.X, directionalLight.Color.Y, directionalLight.Color.Z);
            GL.Uniform1(_dirLightIntensityLocation, directionalLight.Intensity);
            GL.Uniform1(_dirLightAmbientStrengthLocation, directionalLight.AmbientStrength);
        }
        else
        {
            GL.Uniform1(_hasDirLightLocation, 0);
        }

        GL.Uniform1(_numPointLightsLocation, pointLights.Count);
        for (int i = 0; i < pointLights.Count; i++)
        {
            var light = pointLights[i];
            float range = Math.Max(0.1f, light.Range); // Prevent division by zero
            float linear = 4.5f / range;
            float quadratic = 75.0f / (range * range);

            GL.Uniform3(_pointLightPosLocations[i], light.GameObject.Transform.Position.X, light.GameObject.Transform.Position.Y, light.GameObject.Transform.Position.Z);
            GL.Uniform3(_pointLightColorLocations[i], light.Color.X, light.Color.Y, light.Color.Z);
            GL.Uniform1(_pointLightIntensityLocations[i], light.Intensity);
            GL.Uniform1(_pointLightConstantLocations[i], 1.0f);
            GL.Uniform1(_pointLightLinearLocations[i], linear);
            GL.Uniform1(_pointLightQuadraticLocations[i], quadratic);
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
        GL.Uniform1(_materialSpecularIntensityLocation, material.SpecularIntensity);
        GL.Uniform1(_materialShininessLocation, material.Shininess);

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