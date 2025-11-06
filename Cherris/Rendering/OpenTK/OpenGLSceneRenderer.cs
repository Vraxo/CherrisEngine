using Cherris.Components;
using Cherris.Core;
using OpenTK.Graphics.OpenGL4;
using OpenTK.Mathematics;
using System.Diagnostics;

namespace Cherris;

internal class OpenGLSceneRenderer : IDisposable
{
    private const int MAX_POINT_LIGHTS = 4;
    private const int MAX_SPOT_LIGHTS = 4;

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
    private readonly int[] _pointLightRangeLocations = new int[MAX_POINT_LIGHTS];

    // Spot Light Uniform Locations
    private readonly int _numSpotLightsLocation;
    private readonly int[] _spotLightPosLocations = new int[MAX_SPOT_LIGHTS];
    private readonly int[] _spotLightDirLocations = new int[MAX_SPOT_LIGHTS];
    private readonly int[] _spotLightColorLocations = new int[MAX_SPOT_LIGHTS];
    private readonly int[] _spotLightIntensityLocations = new int[MAX_SPOT_LIGHTS];
    private readonly int[] _spotLightRangeLocations = new int[MAX_SPOT_LIGHTS];
    private readonly int[] _spotLightInnerCutOffLocations = new int[MAX_SPOT_LIGHTS];
    private readonly int[] _spotLightOuterCutOffLocations = new int[MAX_SPOT_LIGHTS];


    public OpenGLSceneRenderer()
    {
        _shaderProgram = ShaderProgram.FromFiles("Shaders/scene.vert", "Shaders/scene.frag");

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
            _pointLightRangeLocations[i] = _shaderProgram.GetUniformLocation($"uPointLights[{i}].range");
        }

        // Spot light uniforms
        _numSpotLightsLocation = _shaderProgram.GetUniformLocation("uNumSpotLights");
        for (int i = 0; i < MAX_SPOT_LIGHTS; i++)
        {
            _spotLightPosLocations[i] = _shaderProgram.GetUniformLocation($"uSpotLights[{i}].position");
            _spotLightDirLocations[i] = _shaderProgram.GetUniformLocation($"uSpotLights[{i}].direction");
            _spotLightColorLocations[i] = _shaderProgram.GetUniformLocation($"uSpotLights[{i}].color");
            _spotLightIntensityLocations[i] = _shaderProgram.GetUniformLocation($"uSpotLights[{i}].intensity");
            _spotLightRangeLocations[i] = _shaderProgram.GetUniformLocation($"uSpotLights[{i}].range");
            _spotLightInnerCutOffLocations[i] = _shaderProgram.GetUniformLocation($"uSpotLights[{i}].innerCutOff");
            _spotLightOuterCutOffLocations[i] = _shaderProgram.GetUniformLocation($"uSpotLights[{i}].outerCutOff");
        }
    }

    public void Render(IEnumerable<GameObject> gameObjects, IEnumerable<Light> lights, Matrix4 view, Matrix4 projection)
    {
        _shaderProgram.Use();

        // Set uniforms that are constant for the frame
        GL.Uniform1(_materialTextureLocation, 0);
        GL.UniformMatrix4(_viewLocation, false, ref view);
        GL.UniformMatrix4(_projectionLocation, false, ref projection);
        var viewPos = view.Inverted().Row3.Xyz;
        GL.Uniform3(_viewPosLocation, viewPos.X, viewPos.Y, viewPos.Z);

        // Find and set light uniforms from the cached list
        var directionalLight = lights.FirstOrDefault(l => l.Type == LightType.Directional);
        var pointLights = lights.Where(l => l.Type == LightType.Point).Take(MAX_POINT_LIGHTS).ToList();
        var spotLights = lights.Where(l => l.Type == LightType.Spot).Take(MAX_SPOT_LIGHTS).ToList();

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
            GL.Uniform3(_pointLightPosLocations[i], light.GameObject.Transform.Position.X, light.GameObject.Transform.Position.Y, light.GameObject.Transform.Position.Z);
            GL.Uniform3(_pointLightColorLocations[i], light.Color.X, light.Color.Y, light.Color.Z);
            GL.Uniform1(_pointLightIntensityLocations[i], light.Intensity);
            GL.Uniform1(_pointLightRangeLocations[i], light.Range);
        }

        GL.Uniform1(_numSpotLightsLocation, spotLights.Count);
        for (int i = 0; i < spotLights.Count; i++)
        {
            var light = spotLights[i];
            var transform = light.GameObject.Transform;
            var direction = transform.Forward;
            GL.Uniform3(_spotLightPosLocations[i], transform.Position.X, transform.Position.Y, transform.Position.Z);
            GL.Uniform3(_spotLightDirLocations[i], direction.X, direction.Y, direction.Z);
            GL.Uniform3(_spotLightColorLocations[i], light.Color.X, light.Color.Y, light.Color.Z);
            GL.Uniform1(_spotLightIntensityLocations[i], light.Intensity);
            GL.Uniform1(_spotLightRangeLocations[i], light.Range);
            GL.Uniform1(_spotLightInnerCutOffLocations[i], MathF.Cos(light.InnerConeAngle * (MathF.PI / 180.0f)));
            GL.Uniform1(_spotLightOuterCutOffLocations[i], MathF.Cos(light.OuterConeAngle * (MathF.PI / 180.0f)));
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