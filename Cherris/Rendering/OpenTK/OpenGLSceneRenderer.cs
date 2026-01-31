using Cherris.Components;
using Cherris.Core;
using OpenTK.Graphics.OpenGL4;
using OpenTK.Mathematics;
using System.Diagnostics;

namespace Cherris.Rendering.OpenTK;

internal class OpenGLSceneRenderer : IDisposable
{
    private const int MAX_POINT_LIGHTS = 4;
    private const int MAX_SPOT_LIGHTS = 4;

    private readonly ShaderProgram _shader;

    // Uniform Locations
    private readonly int _modelLoc, _viewLoc, _projLoc, _viewPosLoc;
    private readonly int _tilingLoc, _emissiveLoc, _specularLoc, _shininessLoc, _textureLoc;

    // Light Uniforms
    private readonly int _hasDirLightLoc;
    private readonly int _dirLightDirLoc, _dirLightColorLoc, _dirLightIntLoc, _dirLightAmbLoc;

    private readonly int _numPointLightsLoc;
    private readonly LightUniforms[] _pointLightUniforms = new LightUniforms[MAX_POINT_LIGHTS];

    private readonly int _numSpotLightsLoc;
    private readonly SpotLightUniforms[] _spotLightUniforms = new SpotLightUniforms[MAX_SPOT_LIGHTS];

    private struct LightUniforms
    {
        public int Pos, Color, Intensity, Range;
    }

    private struct SpotLightUniforms
    {
        public int Pos, Dir, Color, Intensity, Range, InnerCut, OuterCut;
    }

    public OpenGLSceneRenderer()
    {
        _shader = ShaderProgram.FromFiles("Shaders/scene.vert", "Shaders/scene.frag");

        // Matrices
        _modelLoc = _shader.GetUniformLocation("model");
        _viewLoc = _shader.GetUniformLocation("view");
        _projLoc = _shader.GetUniformLocation("projection");
        _viewPosLoc = _shader.GetUniformLocation("uViewPos");

        // Material
        _textureLoc = _shader.GetUniformLocation("uMaterial.texture_diffuse");
        _tilingLoc = _shader.GetUniformLocation("uTiling");
        _emissiveLoc = _shader.GetUniformLocation("uEmissive");
        _specularLoc = _shader.GetUniformLocation("uMaterial.specularIntensity");
        _shininessLoc = _shader.GetUniformLocation("uMaterial.shininess");

        // Directional Light
        _hasDirLightLoc = _shader.GetUniformLocation("uHasDirLight");
        _dirLightDirLoc = _shader.GetUniformLocation("uDirLight.direction");
        _dirLightColorLoc = _shader.GetUniformLocation("uDirLight.color");
        _dirLightIntLoc = _shader.GetUniformLocation("uDirLight.intensity");
        _dirLightAmbLoc = _shader.GetUniformLocation("uDirLight.ambientStrength");

        // Point Lights
        _numPointLightsLoc = _shader.GetUniformLocation("uNumPointLights");
        for (int i = 0; i < MAX_POINT_LIGHTS; i++)
        {
            string baseName = $"uPointLights[{i}]";
            _pointLightUniforms[i] = new LightUniforms
            {
                Pos = _shader.GetUniformLocation($"{baseName}.position"),
                Color = _shader.GetUniformLocation($"{baseName}.color"),
                Intensity = _shader.GetUniformLocation($"{baseName}.intensity"),
                Range = _shader.GetUniformLocation($"{baseName}.range")
            };
        }

        // Spot Lights
        _numSpotLightsLoc = _shader.GetUniformLocation("uNumSpotLights");
        for (int i = 0; i < MAX_SPOT_LIGHTS; i++)
        {
            string baseName = $"uSpotLights[{i}]";
            _spotLightUniforms[i] = new SpotLightUniforms
            {
                Pos = _shader.GetUniformLocation($"{baseName}.position"),
                Dir = _shader.GetUniformLocation($"{baseName}.direction"),
                Color = _shader.GetUniformLocation($"{baseName}.color"),
                Intensity = _shader.GetUniformLocation($"{baseName}.intensity"),
                Range = _shader.GetUniformLocation($"{baseName}.range"),
                InnerCut = _shader.GetUniformLocation($"{baseName}.innerCutOff"),
                OuterCut = _shader.GetUniformLocation($"{baseName}.outerCutOff")
            };
        }
    }

    public void Render(IEnumerable<GameObject> gameObjects, IEnumerable<Light> lights, Matrix4 view, Matrix4 projection)
    {
        _shader.Use();

        // 1. Global Uniforms
        GL.Uniform1(_textureLoc, 0);
        GL.UniformMatrix4(_viewLoc, false, ref view);
        GL.UniformMatrix4(_projLoc, false, ref projection);

        var viewPos = view.Inverted().Row3.Xyz;
        GL.Uniform3(_viewPosLoc, viewPos.X, viewPos.Y, viewPos.Z);

        // 2. Lights
        UploadLights(lights);

        // 3. Objects
        foreach (var go in gameObjects)
        {
            var mr = go.GetComponent<MeshRenderer>();
            if (mr?.Mesh is null || go.GetComponent<Skybox>() is not null)
            {
                continue;
            }

            DrawObject(go, mr);
        }
    }

    private void UploadLights(IEnumerable<Light> lights)
    {
        var dirLight = lights.FirstOrDefault(l => l.Type == LightType.Directional);

        if (dirLight != null)
        {
            GL.Uniform1(_hasDirLightLoc, 1);
            var dir = Vector3.Normalize(Vector3.Transform(-Vector3.UnitZ, dirLight.GameObject.Transform.Rotation.ToOpenTK()));

            GL.Uniform3(_dirLightDirLoc, -dir); // Direction *to* light
            GL.Uniform3(_dirLightColorLoc, dirLight.Color.ToOpenTK());
            GL.Uniform1(_dirLightIntLoc, dirLight.Intensity);
            GL.Uniform1(_dirLightAmbLoc, dirLight.AmbientStrength);
        }
        else
        {
            GL.Uniform1(_hasDirLightLoc, 0);
        }

        var points = lights.Where(l => l.Type == LightType.Point).Take(MAX_POINT_LIGHTS).ToList();
        GL.Uniform1(_numPointLightsLoc, points.Count);

        for (int i = 0; i < points.Count; i++)
        {
            var l = points[i];
            var u = _pointLightUniforms[i];
            GL.Uniform3(u.Pos, l.GameObject.Transform.Position.ToOpenTK());
            GL.Uniform3(u.Color, l.Color.ToOpenTK());
            GL.Uniform1(u.Intensity, l.Intensity);
            GL.Uniform1(u.Range, l.Range);
        }

        var spots = lights.Where(l => l.Type == LightType.Spot).Take(MAX_SPOT_LIGHTS).ToList();
        GL.Uniform1(_numSpotLightsLoc, spots.Count);

        for (int i = 0; i < spots.Count; i++)
        {
            var l = spots[i];
            var u = _spotLightUniforms[i];
            var transform = l.GameObject.Transform;

            GL.Uniform3(u.Pos, transform.Position.ToOpenTK());
            GL.Uniform3(u.Dir, transform.Forward.ToOpenTK());
            GL.Uniform3(u.Color, l.Color.ToOpenTK());
            GL.Uniform1(u.Intensity, l.Intensity);
            GL.Uniform1(u.Range, l.Range);
            GL.Uniform1(u.InnerCut, MathF.Cos(l.InnerConeAngle * (MathF.PI / 180.0f)));
            GL.Uniform1(u.OuterCut, MathF.Cos(l.OuterConeAngle * (MathF.PI / 180.0f)));
        }
    }

    private void DrawObject(GameObject go, MeshRenderer mr)
    {
        var data = GetOrCreateBackendData(mr);
        var mat = mr.Material;

        // Texture
        if (mat.Texture is OpenTKTexture glTexture)
        {
            glTexture.Bind(TextureUnit.Texture0);
        }
        else
        {
            GL.BindTexture(TextureTarget.Texture2D, 0);
        }

        // Material Uniforms
        var model = ToOpenTKMatrix(go.Transform.GetModelMatrix());
        GL.UniformMatrix4(_modelLoc, false, ref model);
        GL.Uniform2(_tilingLoc, mat.TextureTiling.X, mat.TextureTiling.Y);
        GL.Uniform3(_emissiveLoc, mat.EmissiveColor.X, mat.EmissiveColor.Y, mat.EmissiveColor.Z);
        GL.Uniform1(_specularLoc, mat.SpecularIntensity);
        GL.Uniform1(_shininessLoc, mat.Shininess);

        // Draw
        GL.BindVertexArray(data.VaoHandle);
        GL.DrawElements(PrimitiveType.Triangles, data.IndexCount, DrawElementsType.UnsignedShort, 0);
        GL.BindVertexArray(0);

        CheckGLError($"Draw '{go.Name}'");
    }

    private OpenGLMeshRendererData GetOrCreateBackendData(MeshRenderer mr)
    {
        if (mr.BackendData is OpenGLMeshRendererData d)
        {
            return d;
        }

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
        _shader.Dispose();
    }
}

// Helpers Extensions for this file
file static class MathExtensions
{
    public static Vector3 ToOpenTK(this System.Numerics.Vector3 v)
    {
        return new(v.X, v.Y, v.Z);
    }

    public static Quaternion ToOpenTK(this System.Numerics.Quaternion q)
    {
        return new(q.X, q.Y, q.Z, q.W);
    }
}