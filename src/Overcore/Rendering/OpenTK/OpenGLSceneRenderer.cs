using Overcore.Animation;
using Overcore.Components;
using Overcore.Core;
using Overcore.Rendering.Shadows;
using OpenTK.Graphics.OpenGL4;
using OpenTK.Mathematics;
using System.Diagnostics;

namespace Overcore.Rendering.OpenTK;

internal class OpenGLSceneRenderer : IDisposable
{
    private const int MaxPointLights = 4;
    private const int MaxSpotLights = 4;

    /// <summary>The cascade array's length in the shader, which GLSL 330 requires to be a literal.</summary>
    private const int MaxShadowCascades = 4;

    /// <summary>The bone palette's unit. Away from 0, which the material's diffuse texture owns.</summary>
    private const TextureUnit BoneTextureUnit = TextureUnit.Texture1;

    /// <summary>
    /// The shadow map's unit. Beside the other two rather than inside <c>Program</c>, because the outer class binds and
    /// unbinds it as well as the program that samples it.
    /// </summary>
    private const TextureUnit ShadowMapTextureUnit = TextureUnit.Texture2;

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

        /// <summary>
        /// The receiver's uniforms. A struct rather than a block of locals so each program keeps its own indices: the
        /// static and skinning programs happen to agree today and are not required to.
        /// </summary>
        /// <remarks>
        /// The settings type is written out in full wherever it is named in this file. A bare <c>Shadows</c> in
        /// <c>Overcore.Rendering.OpenTK</c> binds to the <c>Overcore.Rendering.Shadows</c> <em>namespace</em>, not to the
        /// component inside it, so <c>Shadows.PcfTexels</c> would be a member lookup on a namespace.
        /// </remarks>
        private struct ShadowUniforms
        {
            public int Map;
            public int Matrices;
            public int Splits;
            public int Count;
            public int Distance;
            public int NearPlane;
            public int Bias;
            public int NormalBias;
            public int Strength;
            public int Softness;
            public int BlendBand;
            public int TexelSize;
            public int Receive;
            public int CameraForward;
        }

        private readonly ShadowUniforms _shadow;

        /// <summary>One cascade's four matrices, uploaded as one array. Shared, because it is per frame.</summary>
        private readonly float[] _shadowMatrixScratch = new float[MaxShadowCascades * 16];

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

            _shadow = new()
            {
                Map = Location("uShadowMap"),
                Matrices = Location("uShadowMatrices"),
                Splits = Location("uCascadeSplits"),
                Count = Location("uCascadeCount"),
                Distance = Location("uShadowDistance"),
                NearPlane = Location("uShadowNearPlane"),
                Bias = Location("uShadowBias"),
                NormalBias = Location("uShadowNormalBias"),
                Strength = Location("uShadowStrength"),
                Softness = Location("uShadowSoftness"),
                BlendBand = Location("uShadowBlendBand"),
                TexelSize = Location("uShadowTexelSize"),
                Receive = Location("uReceiveShadows"),
                CameraForward = Location("uCameraForward")
            };

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


        /// <summary>
        /// Uploads everything the fragment stage needs to sample the depth map, for one frame.
        /// </summary>
        /// <param name="frame">The frame the depth pass just rendered, or <see cref="ShadowFrame.None"/>.</param>
        /// <param name="mapTexture">The depth texture the pass wrote, or zero for none.</param>
        /// <param name="cameraForward">The eye's forward axis, for turning a world position into a view depth.</param>
        /// <param name="receive">Whether the surface being drawn receives shadows at all.</param>
        public void UploadShadows(ShadowFrame frame, int mapTexture, Vector3 cameraForward, bool receive)
        {
            // Every early return below leaves the same state behind: receive off, count zero. The receiver's first
            // branch is then taken for any fragment, so a uniform that was never uploaded cannot shadow anything.
            if (!frame.Enabled || mapTexture == 0 || !receive)
            {
                GL.Uniform1(_shadow.Receive, 0);
                GL.Uniform1(_shadow.Count, 0);
                GL.ActiveTexture(TextureUnit.Texture0);

                return;
            }

            Overcore.Rendering.Shadows.Shadows settings = frame.Settings;
            Light light = frame.Light!;
            int cascades = Math.Min(frame.Cascades.Count, MaxShadowCascades);

            GL.ActiveTexture(ShadowMapTextureUnit);
            GL.BindTexture(TextureTarget.Texture2DArray, mapTexture);
            GL.Uniform1(_shadow.Map, (int)ShadowMapTextureUnit);
            GL.Uniform1(_shadow.Receive, 1);

            // The array is uploaded whole and in column-major order, which is what glUniformMatrix4fv expects and what
            // a System.Numerics matrix is not: that type is row-vector, so its fields are already in the order a
            // column-major upload wants and each row of sixteen writes one cascade.
            for (int cascade = 0; cascade < cascades; cascade++)
            {
                System.Numerics.Matrix4x4 m = frame.Cascades[cascade].LightViewProjection;
                int o = cascade * 16;

                _shadowMatrixScratch[o + 0] = m.M11;
                _shadowMatrixScratch[o + 1] = m.M12;
                _shadowMatrixScratch[o + 2] = m.M13;
                _shadowMatrixScratch[o + 3] = m.M14;
                _shadowMatrixScratch[o + 4] = m.M21;
                _shadowMatrixScratch[o + 5] = m.M22;
                _shadowMatrixScratch[o + 6] = m.M23;
                _shadowMatrixScratch[o + 7] = m.M24;
                _shadowMatrixScratch[o + 8] = m.M31;
                _shadowMatrixScratch[o + 9] = m.M32;
                _shadowMatrixScratch[o + 10] = m.M33;
                _shadowMatrixScratch[o + 11] = m.M34;
                _shadowMatrixScratch[o + 12] = m.M41;
                _shadowMatrixScratch[o + 13] = m.M42;
                _shadowMatrixScratch[o + 14] = m.M43;
                _shadowMatrixScratch[o + 15] = m.M44;
            }

            GL.UniformMatrix4(_shadow.Matrices, cascades, false, _shadowMatrixScratch);

            // Splits as absolute depths, not fractions: the shader compares against a world-space view depth, and a
            // fraction would have to be multiplied by the distance in the shader with no way to tell if it had been.
            // A cascade beyond the count repeats the last split, so the vec4 is always fully initialised.
            float[] far = settings.FarDepths();
            float last = far.Length > 0 ? far[^1] : settings.Distance;

            GL.Uniform4(
                _shadow.Splits,
                At(far, 0, last),
                At(far, 1, last),
                At(far, 2, last),
                At(far, 3, last));

            GL.Uniform1(_shadow.Count, cascades);
            GL.Uniform1(_shadow.Distance, cascades > 0 ? far[cascades - 1] : settings.Distance);

            // A *view* depth, so this is the camera's near plane and not CascadeFit.NearPlane — that one is a distance
            // along the light's forward axis, and comparing a camera depth against it would discard fragments for no
            // reason anybody could see. Read from the light rather than from frame.Ranges[0].Near, which was the bug
            // this replaced: the light's setting was declared, serialized, drawn in the inspector, and never uploaded.
            GL.Uniform1(_shadow.NearPlane, light.ShadowNearPlane);

            GL.Uniform1(_shadow.Bias, light.ShadowBias);
            GL.Uniform1(_shadow.NormalBias, light.ShadowNormalBias);
            GL.Uniform1(_shadow.Strength, light.ShadowStrength);

            // Soft: a 3x3 tent, one texel wide. Hard: one fetch. That is the whole difference between the two modes,
            // which is the honest amount of difference a filter this size can claim. The tent's width is a constant
            // rather than a scene setting (§6.2), and it is in *texels*, so a smaller map is both coarser and softer —
            // the penumbra's width in world units follows the texel size, which is the behaviour a reader would expect
            // and the one an earlier comment here described backwards.
            GL.Uniform1(_shadow.Softness, light.Shadows == ShadowType.Soft ? Overcore.Rendering.Shadows.Shadows.PcfTexels : 0f);
            GL.Uniform1(_shadow.BlendBand, light.Shadows == ShadowType.Soft ? Overcore.Rendering.Shadows.Shadows.BlendBand : 0f);

            // Texel size in world units, one per cascade. All four, not just the first: the offset is scaled by the
            // cascade's own texel, and one texel is 0.0125 m in the innermost cascade against 0.177 m in the outermost,
            // so reusing the first value there makes the outer cascades acne-prone by a factor of fourteen.
            GL.Uniform4(
                _shadow.TexelSize,
                TexelSizeOf(frame, 0),
                TexelSizeOf(frame, 1),
                TexelSizeOf(frame, 2),
                TexelSizeOf(frame, 3));

            GL.Uniform3(_shadow.CameraForward, cameraForward);

            // Back to the material's unit, so the draw that follows binds its diffuse where it expects to.
            GL.ActiveTexture(TextureUnit.Texture0);
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

    /// <summary>One split, or <paramref name="fallback"/> when the list is shorter than the shader's vec4.</summary>
    private static float At(float[] splits, int index, float fallback) =>
        index < splits.Length ? splits[index] : fallback;

    /// <summary>
    /// One cascade's world units per texel, or the last cascade's when the shader's vec4 is longer than the frame —
    /// so an unused slot carries a sane number rather than zero, which would silently disable the normal offset.
    /// </summary>
    private static float TexelSizeOf(ShadowFrame frame, int cascade)
    {
        int count = frame.Cascades.Count;

        if (count == 0)
        {
            return 0f;
        }

        return frame.Cascades[Math.Min(cascade, count - 1)].TexelSize;
    }

    /// <summary>
    /// Draws the scene's lit geometry, sampling the shadow map the depth pass has already rendered.
    /// </summary>
    /// <param name="gameObjects">The scene's objects.</param>
    /// <param name="lights">The scene's lights.</param>
    /// <param name="view">The eye's view matrix.</param>
    /// <param name="projection">The eye's projection matrix.</param>
    /// <param name="shadows">The frame the depth pass drew, or <see cref="ShadowFrame.None"/>.</param>
    /// <param name="shadowTexture">The depth texture, or zero when the pass was skipped.</param>
    public void Render(
        IEnumerable<GameObject> gameObjects,
        IEnumerable<Light> lights,
        Matrix4 view,
        Matrix4 projection,
        ShadowFrame shadows,
        int shadowTexture = 0)
    {
        // Materialised once: each pass walks the list, and the scene's objects are a live collection a script may
        // have added to this frame.
        List<GameObject> objects = [.. gameObjects];
        List<Light> lit = [.. lights];

        // The eye's forward axis, read off the view matrix rather than from the camera: -Z in view space, so the
        // third row of the rotation part, negated. Row 2 of the matrix's world-inverse is the same axis, and taking it
        // from the view here keeps one source for it.
        Vector3 cameraForward = -Vector3.Normalize(new Vector3(
            view.M13,
            view.M23,
            view.M33));

        // One program per pass. A program's uniforms belong to it, so each is given the frame's matrices and lights
        // itself; the alternative is uploading the view matrix into whatever the other program keeps at that index.
        _static.Use();
        _static.UploadGlobals(view, projection);
        _static.UploadLights(lit);

        DrawObjects(objects, shadows, shadowTexture, cameraForward);

        _skinned.Use();
        _skinned.UploadGlobals(view, projection);
        _skinned.UploadLights(lit);

        // The palette lives on its own unit, so it is named once for the program rather than per draw.
        GL.Uniform1(_boneTextureLocation, (int)BoneTextureUnit);

        DrawSkinnedObjects(objects, shadows, shadowTexture, cameraForward);

        // Nothing is left bound to the receiver's unit, so the next frame's first draw cannot sample a stale map.
        GL.ActiveTexture(ShadowMapTextureUnit);
        GL.BindTexture(TextureTarget.Texture2DArray, 0);
        GL.ActiveTexture(TextureUnit.Texture0);
    }

    private void DrawObjects(List<GameObject> objects, ShadowFrame shadows, int shadowTexture, Vector3 cameraForward)
    {
        foreach (GameObject go in objects)
        {
            // A skinned mesh is drawn by the other pass, through the program that reads its bones. A ShadowsOnly
            // caster is drawn by the depth pass alone, which is the entire meaning of that mode.
            if (!ShadowCasters.IsVisible(go)
                || go.GetComponent<SkinnedMesh>() is not null
                || go.GetComponent<MeshRenderer>() is not { } renderer
                || renderer.Mesh is null
                || !renderer.Enabled
                || !go.ActiveInHierarchy
                || go.GetComponent<Skybox>() is not null)
            {
                continue;
            }

            DrawSingleObject(go, renderer, shadows, shadowTexture, cameraForward);
        }
    }

    /// <summary>
    /// Draws the objects a <see cref="SkinnedMesh"/> marks, through the skinning program. The palette is uploaded only
    /// when the pose moved, so a character standing still costs one draw call and no texture traffic.
    /// </summary>
    private void DrawSkinnedObjects(
        List<GameObject> objects,
        ShadowFrame shadows,
        int shadowTexture,
        Vector3 cameraForward)
    {
        foreach (GameObject go in objects)
        {
            if (!ShadowCasters.IsVisible(go)
                || go.GetComponent<SkinnedMesh>()?.Skeleton is not { } skeleton
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

            DrawBound(_skinned, go, renderer, data.VaoHandle, data.IndexCount, shadows, shadowTexture, cameraForward);

            CheckGLError($"Draw skinned '{go.Name}'");
        }
    }

    private void DrawSingleObject(
        GameObject go,
        MeshRenderer renderer,
        ShadowFrame shadows,
        int shadowTexture,
        Vector3 cameraForward)
    {
        OpenGLMeshRendererData data = GetOrCreateBackendData(renderer);

        DrawBound(_static, go, renderer, data.VaoHandle, data.IndexCount, shadows, shadowTexture, cameraForward);

        CheckGLError($"Draw '{go.Name}'");
    }

    /// <summary>Uploads an object's material and model, then draws its bound vertex array.</summary>
    private static void DrawBound(
        Program program,
        GameObject go,
        MeshRenderer renderer,
        int vaoHandle,
        int indexCount,
        ShadowFrame shadows,
        int shadowTexture,
        Vector3 cameraForward)
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

        // Per object, because it is per object's choice: a renderer with ReceiveShadows off keeps the shadow pass's
        // texture bound but drives the receiver's first branch, so its fragments are never darkened.
        program.UploadShadows(shadows, shadowTexture, cameraForward, renderer.ReceiveShadows);

        GL.BindVertexArray(vaoHandle);
        GL.DrawElements(PrimitiveType.Triangles, indexCount, DrawElementsType.UnsignedInt, 0);
        GL.BindVertexArray(0);
    }

    internal OpenGLSkinnedMeshData GetOrCreateSkinnedData(MeshRenderer renderer, Skeleton skeleton)
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

    internal OpenGLMeshRendererData GetOrCreateBackendData(MeshRenderer mr)
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