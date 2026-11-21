using Cherris.Components;
using Cherris.Core;
using OpenTK.Graphics.OpenGL4;
using OpenTK.Mathematics;
using System.Diagnostics;

namespace Cherris.Rendering.OpenTK;

internal class OpenGLSceneRenderer : IDisposable
{
    private const int MaxPointLights = 4;
    private const int MaxSpotLights = 4;

    private readonly ShaderProgram _shader;

    private struct MatrixUniforms
    {
        public int Model;
        public int View;
        public int Projection;
        public int ViewPosition;
    }

    private struct MaterialUniforms
    {
        public int Texture;
        public int Tiling;
        public int Emissive;
        public int Specular;
        public int Shininess;
    }

    private struct DirLightUniforms
    {
        public int HasLight;
        public int Direction;
        public int Color;
        public int Intensity;
        public int Ambient;
    }

    private struct PointLightUniforms
    {
        public int Position;
        public int Color;
        public int Intensity;
        public int Range;
    }

    private struct SpotLightUniforms
    {
        public int Position;
        public int Direction;
        public int Color;
        public int Intensity;
        public int Range;
        public int InnerCut;
        public int OuterCut;
    }

    private readonly MatrixUniforms _matrices;
    private readonly MaterialUniforms _material;
    private readonly DirLightUniforms _dirLight;

    private readonly int _numPointLightsLoc;
    private readonly PointLightUniforms[] _pointLights = new PointLightUniforms[MaxPointLights];

    private readonly int _numSpotLightsLoc;
    private readonly SpotLightUniforms[] _spotLights = new SpotLightUniforms[MaxSpotLights];

    public OpenGLSceneRenderer()
    {
        _shader = ShaderProgram.FromFiles("Shaders/scene.vert", "Shaders/scene.frag")
                 ?? throw new InvalidOperationException("Failed to load scene shaders.");

        _matrices = new()
        {
            Model = _shader.GetUniformLocation("model"),
            View = _shader.GetUniformLocation("view"),
            Projection = _shader.GetUniformLocation("projection"),
            ViewPosition = _shader.GetUniformLocation("uViewPos")
        };

        _material = new()
        {
            Texture = _shader.GetUniformLocation("uMaterial.texture_diffuse"),
            Tiling = _shader.GetUniformLocation("uTiling"),
            Emissive = _shader.GetUniformLocation("uEmissive"),
            Specular = _shader.GetUniformLocation("uMaterial.specularIntensity"),
            Shininess = _shader.GetUniformLocation("uMaterial.shininess")
        };

        _dirLight = new()
        {
            HasLight = _shader.GetUniformLocation("uHasDirLight"),
            Direction = _shader.GetUniformLocation("uDirLight.direction"),
            Color = _shader.GetUniformLocation("uDirLight.color"),
            Intensity = _shader.GetUniformLocation("uDirLight.intensity"),
            Ambient = _shader.GetUniformLocation("uDirLight.ambientStrength")
        };

        _numPointLightsLoc = _shader.GetUniformLocation("uNumPointLights");
        for (int i = 0; i < MaxPointLights; i++)
        {
            string baseName = $"uPointLights[{i}]";
            _pointLights[i] = new PointLightUniforms
            {
                Position = _shader.GetUniformLocation($"{baseName}.position"),
                Color = _shader.GetUniformLocation($"{baseName}.color"),
                Intensity = _shader.GetUniformLocation($"{baseName}.intensity"),
                Range = _shader.GetUniformLocation($"{baseName}.range")
            };
        }

        _numSpotLightsLoc = _shader.GetUniformLocation("uNumSpotLights");
        for (int i = 0; i < MaxSpotLights; i++)
        {
            string baseName = $"uSpotLights[{i}]";
            _spotLights[i] = new SpotLightUniforms
            {
                Position = _shader.GetUniformLocation($"{baseName}.position"),
                Direction = _shader.GetUniformLocation($"{baseName}.direction"),
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

        SetupGlobalUniforms(view, projection);
        UploadLights(lights);
        DrawObjects(gameObjects);
    }

    private void SetupGlobalUniforms(Matrix4 view, Matrix4 projection)
    {
        GL.Uniform1(_material.Texture, 0);
        GL.UniformMatrix4(_matrices.View, false, ref view);
        GL.UniformMatrix4(_matrices.Projection, false, ref projection);

        var viewPos = view.Inverted().Row3.Xyz;
        GL.Uniform3(_matrices.ViewPosition, viewPos.X, viewPos.Y, viewPos.Z);
    }

    private void UploadLights(IEnumerable<Light> lights)
    {
        // Directional Light
        var dirLight = lights.FirstOrDefault(l => l.Type == LightType.Directional);
        if (dirLight != null)
        {
            GL.Uniform1(_dirLight.HasLight, 1);
            var dir = Vector3.Normalize(Vector3.Transform(-Vector3.UnitZ, dirLight.GameObject.Transform.Rotation.ToOpenTK()));

            GL.Uniform3(_dirLight.Direction, -dir); // Direction *to* light
            GL.Uniform3(_dirLight.Color, dirLight.Color.ToOpenTK());
            GL.Uniform1(_dirLight.Intensity, dirLight.Intensity);
            GL.Uniform1(_dirLight.Ambient, dirLight.AmbientStrength);
        }
        else
        {
            GL.Uniform1(_dirLight.HasLight, 0);
        }

        // Point Lights
        var points = lights.Where(l => l.Type == LightType.Point).Take(MaxPointLights).ToList();
        GL.Uniform1(_numPointLightsLoc, points.Count);

        for (int i = 0; i < points.Count; i++)
        {
            var l = points[i];
            var u = _pointLights[i];
            GL.Uniform3(u.Position, l.GameObject.Transform.Position.ToOpenTK());
            GL.Uniform3(u.Color, l.Color.ToOpenTK());
            GL.Uniform1(u.Intensity, l.Intensity);
            GL.Uniform1(u.Range, l.Range);
        }

        // Spot Lights
        var spots = lights.Where(l => l.Type == LightType.Spot).Take(MaxSpotLights).ToList();
        GL.Uniform1(_numSpotLightsLoc, spots.Count);

        for (int i = 0; i < spots.Count; i++)
        {
            var l = spots[i];
            var u = _spotLights[i];
            var transform = l.GameObject.Transform;

            GL.Uniform3(u.Position, transform.Position.ToOpenTK());
            GL.Uniform3(u.Direction, transform.Forward.ToOpenTK());
            GL.Uniform3(u.Color, l.Color.ToOpenTK());
            GL.Uniform1(u.Intensity, l.Intensity);
            GL.Uniform1(u.Range, l.Range);
            GL.Uniform1(u.InnerCut, MathF.Cos(l.InnerConeAngle * (MathF.PI / 180.0f)));
            GL.Uniform1(u.OuterCut, MathF.Cos(l.OuterConeAngle * (MathF.PI / 180.0f)));
        }
    }

    private void DrawObjects(IEnumerable<GameObject> gameObjects)
    {
        foreach (var go in gameObjects)
        {
            var mr = go.GetComponent<MeshRenderer>();

            // Skip invalid meshes or skyboxes (skyboxes are handled by SkyboxRenderer)
            if (mr?.Mesh is null || go.GetComponent<Skybox>() is not null)
            {
                continue;
            }

            DrawSingleObject(go, mr);
        }
    }

    private void DrawSingleObject(GameObject go, MeshRenderer mr)
    {
        var data = GetOrCreateBackendData(mr);
        var mat = mr.Material;

        // Texture binding
        if (mat.Texture is OpenTKTexture glTexture)
        {
            glTexture.Bind(TextureUnit.Texture0);
        }
        else
        {
            GL.BindTexture(TextureTarget.Texture2D, 0);
        }

        // Material uniforms
        var model = ToOpenTKMatrix(go.Transform.GetModelMatrix());
        GL.UniformMatrix4(_matrices.Model, false, ref model);
        GL.Uniform2(_material.Tiling, mat.TextureTiling.X, mat.TextureTiling.Y);
        GL.Uniform3(_material.Emissive, mat.EmissiveColor.X, mat.EmissiveColor.Y, mat.EmissiveColor.Z);
        GL.Uniform1(_material.Specular, mat.SpecularIntensity);
        GL.Uniform1(_material.Shininess, mat.Shininess);

        // Draw call
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