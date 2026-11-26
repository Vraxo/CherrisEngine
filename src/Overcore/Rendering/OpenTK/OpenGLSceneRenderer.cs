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

    /// <summary>The bone palette's unit. Away from 0, which the material's diffuse texture owns.</summary>
    private const TextureUnit BoneTextureUnit = TextureUnit.Texture1;

    /// <summary>
    /// One shader program and the uniform locations that belong to it. Locations are indices into a <em>specific</em>
    /// program, so the static and skinning programs each need their own set: sharing one cache would upload the view
    /// matrix to whatever the other program happens to keep at that index.
    /// </summary>
    private sealed class Program : IDisposable
    {
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

        private readonly ShaderProgram _shader;
        private readonly MatrixUniforms _matrices;
        private readonly MaterialUniforms _material;
        private readonly DirLightUniforms _dirLight;
        private readonly int _numPointLights;
        private readonly PointLightUniforms[] _pointLights = new PointLightUniforms[MaxPointLights];
        private readonly int _numSpotLights;
        private readonly SpotLightUniforms[] _spotLights = new SpotLightUniforms[MaxSpotLights];

        public Program(string vertexPath, string fragmentPath, string description)
        {
            _shader = ShaderProgram.FromFiles(vertexPath, fragmentPath)
                     ?? throw new InvalidOperationException($"Failed to load {description} shaders.");

            _matrices = new()
            {
                Model = Location("model"),
                View = Location("view"),
                Projection = Location("projection"),
                ViewPosition = Location("uViewPos")
            };

            _material = new()
            {
                Texture = Location("uMaterial.texture_diffuse"),
                Tiling = Location("uTiling"),
                Emissive = Location("uEmissive"),
                Specular = Location("uMaterial.specularIntensity"),
                Shininess = Location("uMaterial.shininess")
            };

            _dirLight = new()
            {
                HasLight = Location("uHasDirLight"),
                Direction = Location("uDirLight.direction"),
                Color = Location("uDirLight.color"),
                Intensity = Location("uDirLight.intensity"),
                Ambient = Location("uDirLight.ambientStrength")
            };

            _numPointLights = Location("uNumPointLights");
            for (int i = 0; i < MaxPointLights; i++)
            {
                string baseName = $"uPointLights[{i}]";

                _pointLights[i] = new PointLightUniforms
                {
                    Position = Location($"{baseName}.position"),
                    Color = Location($"{baseName}.color"),
                    Intensity = Location($"{baseName}.intensity"),
                    Range = Location($"{baseName}.range")
                };
            }

            _numSpotLights = Location("uNumSpotLights");
            for (int i = 0; i < MaxSpotLights; i++)
            {
                string baseName = $"uSpotLights[{i}]";

                _spotLights[i] = new SpotLightUniforms
                {
                    Position = Location($"{baseName}.position"),
                    Direction = Location($"{baseName}.direction"),
                    Color = Location($"{baseName}.color"),
                    Intensity = Location($"{baseName}.intensity"),
                    Range = Location($"{baseName}.range"),
                    InnerCut = Location($"{baseName}.innerCutOff"),
                    OuterCut = Location($"{baseName}.outerCutOff")
                };
            }
        }

        /// <summary>A uniform location in <em>this</em> program, which is why it is asked here and not anywhere else.</summary>
        public int Location(string name) => _shader.GetUniformLocation(name);

        public void Use() => _shader.Use();

        public void UploadGlobals(Matrix4 view, Matrix4 projection)
        {
            GL.Uniform1(_material.Texture, 0);
            GL.UniformMatrix4(_matrices.View, false, ref view);
            GL.UniformMatrix4(_matrices.Projection, false, ref projection);

            var viewPos = view.Inverted().Row3.Xyz;
            GL.Uniform3(_matrices.ViewPosition, viewPos.X, viewPos.Y, viewPos.Z);
        }

        public void UploadMaterial(Material material, Matrix4 model)
        {
            GL.UniformMatrix4(_matrices.Model, false, ref model);
            GL.Uniform2(_material.Tiling, material.TextureTiling.X, material.TextureTiling.Y);
            GL.Uniform3(_material.Emissive, material.EmissiveColor.X, material.EmissiveColor.Y, material.EmissiveColor.Z);
            GL.Uniform1(_material.Specular, material.SpecularIntensity);
            GL.Uniform1(_material.Shininess, material.Shininess);
        }

        public void UploadLights(IReadOnlyList<Light> lights)
        {
            Light? dirLight = lights.FirstOrDefault(l => l.Type == LightType.Directional);

            if (dirLight is not null)
            {
                GL.Uniform1(_dirLight.HasLight, 1);

                Vector3 dir = Vector3.Normalize(
                    Vector3.Transform(-Vector3.UnitZ, dirLight.GameObject.Transform.Rotation.ToOpenTK()));

                GL.Uniform3(_dirLight.Direction, -dir); // Direction *to* light
                GL.Uniform3(_dirLight.Color, dirLight.Color.ToOpenTK());
                GL.Uniform1(_dirLight.Intensity, dirLight.Intensity);
                GL.Uniform1(_dirLight.Ambient, dirLight.AmbientStrength);
            }
            else
            {
                GL.Uniform1(_dirLight.HasLight, 0);
            }

            List<Light> points = lights.Where(l => l.Type == LightType.Point).Take(MaxPointLights).ToList();
            GL.Uniform1(_numPointLights, points.Count);

            for (int i = 0; i < points.Count; i++)
            {
                Light light = points[i];
                PointLightUniforms u = _pointLights[i];

                GL.Uniform3(u.Position, light.GameObject.Transform.Position.ToOpenTK());
                GL.Uniform3(u.Color, light.Color.ToOpenTK());
                GL.Uniform1(u.Intensity, light.Intensity);
                GL.Uniform1(u.Range, light.Range);
            }

            List<Light> spots = lights.Where(l => l.Type == LightType.Spot).Take(MaxSpotLights).ToList();
            GL.Uniform1(_numSpotLights, spots.Count);

            for (int i = 0; i < spots.Count; i++)
            {
                Light light = spots[i];
                SpotLightUniforms u = _spotLights[i];
                Transform transform = light.GameObject.Transform;

                GL.Uniform3(u.Position, transform.Position.ToOpenTK());
                GL.Uniform3(u.Direction, transform.Forward.ToOpenTK());
                GL.Uniform3(u.Color, light.Color.ToOpenTK());
                GL.Uniform1(u.Intensity, light.Intensity);
                GL.Uniform1(u.Range, light.Range);
                GL.Uniform1(u.InnerCut, MathF.Cos(light.InnerConeAngle * (MathF.PI / 180.0f)));
                GL.Uniform1(u.OuterCut, MathF.Cos(light.OuterConeAngle * (MathF.PI / 180.0f)));
            }
        }

        public void Dispose() => _shader.Dispose();
    }

    private readonly Program _static;
    private readonly Program _skinned;
    private readonly int _boneTextureLocation;

    public OpenGLSceneRenderer()
    {
        _static = new Program("Shaders/scene.vert", "Shaders/scene.frag", "scene");

        // The skinning program: the same lighting, with the bone palette applied in the vertex stage. A second program
        // rather than a branch, because the branch would cost every static vertex a texture fetch it never uses.
        _skinned = new Program("Shaders/skin.vert", "Shaders/skin.frag", "skinning");

        _boneTextureLocation = _skinned.Location("uBoneTexture");
    }

    public void Render(IEnumerable<GameObject> gameObjects, IEnumerable<Light> lights, Matrix4 view, Matrix4 projection)
    {
        // Materialised once: each pass walks the list, and the scene's objects are a live collection a script may
        // have added to this frame.
        List<GameObject> objects = [.. gameObjects];
        List<Light> lit = [.. lights];

        // One program per pass. A program's uniforms belong to it, so each is given the frame's matrices and lights
        // itself; the alternative is uploading the view matrix into whatever the other program keeps at that index.
        _static.Use();
        _static.UploadGlobals(view, projection);
        _static.UploadLights(lit);

        DrawObjects(objects);

        _skinned.Use();
        _skinned.UploadGlobals(view, projection);
        _skinned.UploadLights(lit);

        // The palette lives on its own unit, so it is named once for the program rather than per draw.
        GL.Uniform1(_boneTextureLocation, (int)BoneTextureUnit);

        DrawSkinnedObjects(objects);
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

            DrawBound(_skinned, go, renderer, data.VaoHandle, data.IndexCount);

            CheckGLError($"Draw skinned '{go.Name}'");
        }
    }

    private void DrawSingleObject(GameObject go, MeshRenderer renderer)
    {
        OpenGLMeshRendererData data = GetOrCreateBackendData(renderer);

        DrawBound(_static, go, renderer, data.VaoHandle, data.IndexCount);

        CheckGLError($"Draw '{go.Name}'");
    }

    /// <summary>Uploads an object's material and model, then draws its bound vertex array.</summary>
    private static void DrawBound(Program program, GameObject go, MeshRenderer renderer, int vaoHandle, int indexCount)
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

        program.UploadMaterial(material, ToOpenTKMatrix(go.Transform.GetModelMatrix()));

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
        _static.Dispose();
        _skinned.Dispose();
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