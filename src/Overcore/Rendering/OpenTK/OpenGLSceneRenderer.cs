using Overcore.Animation;
using Overcore.Components;
using Overcore.Core;
using OpenTK.Graphics.OpenGL4;
using OpenTK.Mathematics;
using System.Diagnostics;

namespace Overcore.Rendering.OpenTK;

internal class OpenGLSceneRenderer : IDisposable
{
    private const int MaxPointLights = 4;
    private const int MaxSpotLights = 4;

    private readonly ShaderProgram _shader;

    /// <summary>
    /// The skinning program: the same lighting, with the bone palette applied in the vertex stage. A separate program
    /// rather than a branch, because the branch would cost every static vertex a texture fetch it never uses.
    /// </summary>
    private readonly ShaderProgram _skinnedShader;

    /// <summary>The unit the bone palette lives on. Away from texture 0, which the material's diffuse texture owns.</summary>
    private const TextureUnit BoneTextureUnit = TextureUnit.Texture1;

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

    private readonly int _boneTextureLocation;

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

        _skinnedShader = ShaderProgram.FromFiles("Shaders/skin.vert", "Shaders/skin.frag")
                      ?? throw new InvalidOperationException("Failed to load skinning shaders.");

        _boneTextureLocation = _skinnedShader.GetUniformLocation("uBoneTexture");

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
        // Materialised once: each pass walks the list, and the scene's objects are a live collection a script may
        // have added to this frame.
        List<GameObject> objects = [.. gameObjects];
        List<Light> lit = [.. lights];

        // One program per pass, because a shader program's uniforms belong to it: the global block is uploaded twice,
        // which is a handful of calls against a per-object rebind of every light and matrix.
        BeginPass(_shader, view, projection, lit);
        DrawObjects(objects);

        BeginPass(_skinnedShader, view, projection, lit);

        // The palette lives on its own unit, so it is named once for the program rather than per draw.
        GL.Uniform1(_boneTextureLocation, (int)BoneTextureUnit);

        DrawSkinnedObjects(objects);
    }

    /// <summary>Binds a program and gives it the frame's matrices and lights.</summary>
    private void BeginPass(ShaderProgram program, Matrix4 view, Matrix4 projection, List<Light> lights)
    {
        program.Use();

        SetupGlobalUniforms(view, projection);
        UploadLights(lights);
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

    private void DrawObjects(List<GameObject> objects)
    {
        foreach (GameObject go in objects)
        {
            // A skinned mesh is drawn by the other pass, through the program that reads its bones.
            if (go.GetComponent<SkinnedMesh>() is not null
                || go.GetComponent<MeshRenderer>() is not { } renderer
                || renderer.Mesh is null
                || !renderer.Enabled
                || !go.ActiveInHierarchy
                || go.GetComponent<Skybox>() is not null)
            {
                continue;
            }

            DrawSingleObject(go, renderer);
        }
    }

    /// <summary>
    /// Draws the objects a <see cref="SkinnedMesh"/> marks, through the skinning program. The palette is uploaded only
    /// when the pose moved, so a character standing still costs one draw call and no texture traffic.
    /// </summary>
    private void DrawSkinnedObjects(List<GameObject> objects)
    {
        foreach (GameObject go in objects)
        {
            if (go.GetComponent<SkinnedMesh>()?.Skeleton is not { } skeleton
                || go.GetComponent<MeshRenderer>() is not { } renderer
                || renderer.Mesh is null
                || !renderer.Enabled
                || !go.ActiveInHierarchy
                || go.GetComponent<Skybox>() is not null)
            {
                continue;
            }

            OpenGLSkinnedMeshData data = GetOrCreateSkinnedData(renderer, skeleton);

            // The skeleton resolves its joint matrices against the renderer's world, which is what puts the palette
            // in the renderer's parent space and lets the vertex take the ordinary model path afterwards.
            data.UpdatePalette(skeleton, go.Transform.GetModelMatrix());
            data.BoneTexture.Bind();

            DrawBound(go, renderer, data.VaoHandle, data.IndexCount);

            CheckGLError($"Draw skinned '{go.Name}'");
        }
    }

    private void DrawSingleObject(GameObject go, MeshRenderer renderer)
    {
        OpenGLMeshRendererData data = GetOrCreateBackendData(renderer);

        DrawBound(go, renderer, data.VaoHandle, data.IndexCount);

        CheckGLError($"Draw '{go.Name}'");
    }

    /// <summary>Uploads an object's material and model, then draws its bound vertex array.</summary>
    private void DrawBound(GameObject go, MeshRenderer renderer, int vaoHandle, int indexCount)
    {
        Material material = renderer.Material;

        // Texture binding. The material's diffuse owns unit 0; the bone palette owns unit 1.
        if (material.Texture is OpenTKTexture glTexture)
        {
            glTexture.Bind(TextureUnit.Texture0);
        }
        else
        {
            GL.BindTexture(TextureTarget.Texture2D, 0);
        }

        Matrix4 model = ToOpenTKMatrix(go.Transform.GetModelMatrix());

        GL.UniformMatrix4(_matrices.Model, false, ref model);
        GL.Uniform2(_material.Tiling, material.TextureTiling.X, material.TextureTiling.Y);
        GL.Uniform3(_material.Emissive, material.EmissiveColor.X, material.EmissiveColor.Y, material.EmissiveColor.Z);
        GL.Uniform1(_material.Specular, material.SpecularIntensity);
        GL.Uniform1(_material.Shininess, material.Shininess);

        GL.BindVertexArray(vaoHandle);
        GL.DrawElements(PrimitiveType.Triangles, indexCount, DrawElementsType.UnsignedInt, 0);
        GL.BindVertexArray(0);
    }

    private OpenGLSkinnedMeshData GetOrCreateSkinnedData(MeshRenderer renderer, Skeleton skeleton)
    {
        // Keyed off the renderer, like the static data: a clone drops it in OnInstantiated and builds its own.
        if (renderer.SkinnedBackendData is OpenGLSkinnedMeshData existing && existing.BoneCapacity == skeleton.BoneCount)
        {
            return existing;
        }

        (renderer.SkinnedBackendData as IDisposable)?.Dispose();

        OpenGLSkinnedMeshData created = new(renderer.Mesh, skeleton.BoneCount, BoneTextureUnit);
        renderer.SkinnedBackendData = created;

        return created;
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
        _skinnedShader.Dispose();
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