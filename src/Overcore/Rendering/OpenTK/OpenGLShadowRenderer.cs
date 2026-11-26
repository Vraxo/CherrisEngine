using Overcore.Animation;
using Overcore.Components;
using Overcore.Core;
using Overcore.Rendering.Shadows;
using OpenTK.Graphics.OpenGL4;
using OpenTK.Mathematics;

// Both of these namespaces declare a Vector3, and this file needs the engine's arithmetic type for the cull and
// OpenTK's matrix type for the upload. The matrix is the only thing taken from OpenTK.Mathematics.
using Vector3 = System.Numerics.Vector3;

namespace Overcore.Rendering.OpenTK;

/// <summary>
/// The depth pass: draws every caster into each cascade's slice of the shadow texture, and nothing else.
/// </summary>
/// <remarks>
/// <para>
/// Two programs, for the same reason the colour pass has two: a static vertex must not pay for a bone fetch it never
/// uses, and a shader assembled by <c>#define</c> substitution is a shader nobody can read in the repository.
/// </para>
/// <para>
/// No MSAA, no blending, no colour, and front faces culled. Rendering back faces into the depth map moves every
/// surface's recorded depth behind its front face, so a lit surface compares against a depth already behind it — that
/// is the single change that buys the most freedom in the receiver's bias.
/// </para>
/// <para>
/// Casters are culled against each cascade's slice. Without it every caster is drawn into every cascade, which for a
/// scene of 500 objects is 2,500 draws a frame instead of about 500 plus whatever is genuinely huge.
/// </para>
/// </remarks>
internal sealed class OpenGLShadowRenderer : IDisposable
{
    /// <summary>The palette's unit. Unit 0 is the material's diffuse, and the depth pass binds nothing there.</summary>
    private const TextureUnit BoneTextureUnit = TextureUnit.Texture1;

    private readonly ShaderProgram _static;
    private readonly ShaderProgram _skinned;

    private readonly int _staticModel;
    private readonly int _staticLightMatrix;
    private readonly int _skinnedModel;
    private readonly int _skinnedLightMatrix;
    private readonly int _skinnedBoneTexture;

    private ShadowDepthTarget? _target;
    private bool _disposed;

    public OpenGLShadowRenderer()
    {
        _static = ShaderProgram.FromFiles("Shaders/shadow.vert", "Shaders/shadow.frag")
                  ?? throw new InvalidOperationException("Failed to load the shadow depth shaders.");

        // The same vertex stage with the palette applied, so a character's shadow walks when the character does. A
        // caster that is not skinned here stands in the light waving its arms while its shadow stands still.
        _skinned = ShaderProgram.FromFiles("Shaders/shadow_skinned.vert", "Shaders/shadow.frag")
                   ?? throw new InvalidOperationException("Failed to load the skinned shadow depth shaders.");

        _staticModel = _static.GetUniformLocation("model");
        _staticLightMatrix = _static.GetUniformLocation("lightViewProjection");
        _skinnedModel = _skinned.GetUniformLocation("model");
        _skinnedLightMatrix = _skinned.GetUniformLocation("lightViewProjection");
        _skinnedBoneTexture = _skinned.GetUniformLocation("uBoneTexture");
    }

    /// <summary>The texture the receiver samples, or zero when nothing has been rendered.</summary>
    public int Texture => _target?.Texture ?? 0;

    /// <summary>How many layers of <see cref="Texture"/> are real this frame.</summary>
    public int LayerCount => _target?.Layers ?? 0;

    /// <summary>
    /// Draws every caster into each cascade. Leaves the framebuffer unbound and the viewport unset, because the colour
    /// pass sets both and a depth pass that restored a remembered viewport would be restoring the editor's.
    /// </summary>
    /// <param name="frame">The fitted cascades and the settings in force.</param>
    /// <param name="gameObjects">The scene's objects; the caster list is filtered from them.</param>
    /// <param name="rendererData">A renderer's backend data, created on demand.</param>
    /// <param name="skinnedData">A skinned renderer's backend data, created on demand.</param>
    public void Render(
        ShadowFrame frame,
        IEnumerable<GameObject> gameObjects,
        Func<MeshRenderer, OpenGLMeshRendererData> rendererData,
        Func<MeshRenderer, Skeleton, OpenGLSkinnedMeshData> skinnedData)
    {
        ArgumentNullException.ThrowIfNull(frame);
        ArgumentNullException.ThrowIfNull(gameObjects);

        if (!frame.Enabled || frame.Cascades.Count == 0)
        {
            return;
        }

        EnsureTarget(frame.Resolution, frame.Cascades.Count);

        List<GameObject> casters = [.. ShadowCasters.Collect(gameObjects)];

        if (casters.Count == 0)
        {
            // Nothing to draw, but the texture still has to be cleared: a cascade holding the previous frame's depths
            // is an entire level's worth of shadows from a frame ago. Cleared to the far plane, so a receiver sampling
            // it finds nothing occluded rather than everything.
            for (int layer = 0; layer < _target!.Layers; layer++)
            {
                _target.BindLayer(layer);
                _target.Clear();
            }

            GL.BindFramebuffer(FramebufferTarget.Framebuffer, 0);

            return;
        }

        GL.Enable(EnableCap.DepthTest);
        GL.DepthFunc(DepthFunction.Less);
        GL.DepthMask(true);
        GL.Disable(EnableCap.Blend);
        GL.Disable(EnableCap.FramebufferSrgb);
        GL.Enable(EnableCap.CullFace);

        for (int layer = 0; layer < frame.Cascades.Count; layer++)
        {
            CascadeFit fit = frame.Cascades[layer];

            _target!.BindLayer(layer);
            _target.Clear();

            Matrix4 lightMatrix = ToOpenTKMatrix(fit.LightViewProjection);

            GL.CullFace(CullFaceMode.Front);

            _static.Use();
            GL.UniformMatrix4(_staticLightMatrix, false, ref lightMatrix);

            DrawStatic(casters, layer, frame, rendererData);

            // The static pass set a per-object cull state that the last static caster chose, so it is reset before the
            // skinned pass reasons about its own casters. Without this a two-sided caster drawn last leaves culling
            // off for every character in the scene.
            SetBackFaceCulling(true);

            _skinned.Use();
            GL.UniformMatrix4(_skinnedLightMatrix, false, ref lightMatrix);
            GL.Uniform1(_skinnedBoneTexture, (int)BoneTextureUnit);

            DrawSkinned(casters, layer, frame, skinnedData);
        }

        // The colour pass sets its own culling and viewport, so this restores the engine-wide state rather than a
        // remembered one: back faces out, framebuffer unbound.
        GL.CullFace(CullFaceMode.Back);
        GL.BindFramebuffer(FramebufferTarget.Framebuffer, 0);
    }

    private void DrawStatic(
        List<GameObject> casters,
        int layer,
        ShadowFrame frame,
        Func<MeshRenderer, OpenGLMeshRendererData> rendererData)
    {
        foreach (GameObject gameObject in casters)
        {
            if (!IsVisibleTo(gameObject, layer, frame))
            {
                continue;
            }

            if (gameObject.GetComponent<SkinnedMesh>() is not null)
            {
                // The skinned pass draws it, through the program that reads its bones.
                continue;
            }

            MeshRenderer renderer = gameObject.GetComponent<MeshRenderer>()!;

            // Front faces out, so the depth map records the far side of the surface. A two-sided caster has no far
            // side to record, so it draws both.
            SetBackFaceCulling(ShadowCasters.CullsBackFaces(gameObject));

            OpenGLMeshRendererData data = rendererData(renderer);
            Matrix4 model = ToOpenTKMatrix(gameObject.Transform.GetModelMatrix());

            GL.UniformMatrix4(_staticModel, false, ref model);
            GL.BindVertexArray(data.VaoHandle);
            GL.DrawElements(PrimitiveType.Triangles, data.IndexCount, DrawElementsType.UnsignedInt, 0);
        }

        GL.BindVertexArray(0);
    }

    private void DrawSkinned(
        List<GameObject> casters,
        int layer,
        ShadowFrame frame,
        Func<MeshRenderer, Skeleton, OpenGLSkinnedMeshData> skinnedData)
    {
        foreach (GameObject gameObject in casters)
        {
            if (!IsVisibleTo(gameObject, layer, frame))
            {
                continue;
            }

            if (gameObject.GetComponent<SkinnedMesh>()?.Skeleton is not { } skeleton
                || gameObject.GetComponent<MeshRenderer>() is not { } renderer)
            {
                continue;
            }

            SetBackFaceCulling(ShadowCasters.CullsBackFaces(gameObject));

            OpenGLSkinnedMeshData data = skinnedData(renderer, skeleton);

            // The palette is resolved against the renderer's world, exactly as the colour pass does it, so a caster's
            // shadow is the deformed mesh and not its bind pose.
            data.UpdatePalette(skeleton, gameObject.Transform.GetModelMatrix());
            data.BoneTexture.Bind();

            Matrix4 model = ToOpenTKMatrix(gameObject.Transform.GetModelMatrix());

            GL.UniformMatrix4(_skinnedModel, false, ref model);
            GL.BindVertexArray(data.VaoHandle);
            GL.DrawElements(PrimitiveType.Triangles, data.IndexCount, DrawElementsType.UnsignedInt, 0);
        }

        GL.BindVertexArray(0);
    }

    /// <summary>
    /// Whether cascade <paramref name="layer"/> can see this object at all: its world AABB against the slice's eight
    /// corners, conservatively.
    /// </summary>
    /// <remarks>
    /// The test is the object's box against the slice's <em>corner</em> set, widened by the box's own half diagonal —
    /// cheap, and never culls something the slice could have seen. A caster smaller than the padding is drawn in every
    /// cascade, which is the direction a conservative test has to fail in.
    /// </remarks>
    private static bool IsVisibleTo(GameObject gameObject, int layer, ShadowFrame frame)
    {
        IReadOnlyList<Vector3> corners = frame.SliceCorners;

        if (corners.Count < (layer + 1) * 8)
        {
            return true;
        }

        BoundingBox bounds = gameObject.GetWorldSpaceAABB();

        Vector3 centre = (bounds.Min + bounds.Max) * 0.5f;
        float radius = (bounds.Max - bounds.Min).Length() * 0.5f;

        int start = layer * 8;
        Vector3 sliceMin = corners[start];
        Vector3 sliceMax = corners[start];

        for (int corner = start + 1; corner < start + 8; corner++)
        {
            sliceMin = Vector3.Min(sliceMin, corners[corner]);
            sliceMax = Vector3.Max(sliceMax, corners[corner]);
        }

        return centre.X + radius >= sliceMin.X && centre.X - radius <= sliceMax.X
            && centre.Y + radius >= sliceMin.Y && centre.Y - radius <= sliceMax.Y
            && centre.Z + radius >= sliceMin.Z && centre.Z - radius <= sliceMax.Z;
    }

    /// <summary>
    /// Culls the sliver of the mesh facing the light, which is the face the eye would call the back one — so the depth
    /// map stores the surface the light actually fails to reach.
    /// </summary>
    private static void SetBackFaceCulling(bool enabled)
    {
        if (!enabled)
        {
            GL.Disable(EnableCap.CullFace);

            return;
        }

        GL.Enable(EnableCap.CullFace);
        GL.CullFace(CullFaceMode.Front);
    }

    private void EnsureTarget(int resolution, int layers)
    {
        int side = Math.Max(1, resolution);

        if (_target is not null && _target.Resolution == side && _target.Layers == layers)
        {
            return;
        }

        _target?.Dispose();
        _target = new ShadowDepthTarget(side, layers);
    }

    private static Matrix4 ToOpenTKMatrix(System.Numerics.Matrix4x4 m)
    {
        return new Matrix4(
            m.M11, m.M12, m.M13, m.M14,
            m.M21, m.M22, m.M23, m.M24,
            m.M31, m.M32, m.M33, m.M34,
            m.M41, m.M42, m.M43, m.M44);
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _static.Dispose();
        _skinned.Dispose();
        _target?.Dispose();
        _disposed = true;
    }
}
