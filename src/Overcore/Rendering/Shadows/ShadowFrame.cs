using Overcore.Components;
using Overcore.Core;
using System.Numerics;

namespace Overcore.Rendering.Shadows;

/// <summary>
/// Everything one frame's shadow pass needs, gathered once and handed to the renderer.
/// </summary>
/// <remarks>
/// <para>
/// A parameter object rather than three nullable arguments on
/// <see cref="Overcore.Rendering.IRenderer.RenderFrame"/>, because those three travel together and a signature that grew
/// by three more parameters is the signature nobody updates consistently in both backends.
/// </para>
/// <para>
/// <see cref="None"/> is a fully-formed "no shadows this frame": a backend that has not implemented the pass — the
/// Veldrid one, today — is handed it and draws exactly what it drew before, rather than shadowing some frames and not
/// others.
/// </para>
/// </remarks>
public sealed class ShadowFrame
{
    private Vector3[] _corners = [];
    private CascadeFit[] _fits = [];
    private CascadeRange[] _ranges = [];

    /// <summary>Whether anything should be drawn into a depth map this frame.</summary>
    public bool Enabled { get; private set; }

    /// <summary>What per-scene settings were in force, for the numbers a receiver needs.</summary>
    public Shadows Settings { get; private set; } = new();

    /// <summary>The light casting, or null when nothing does.</summary>
    public Light? Light { get; private set; }

    /// <summary>The direction the light travels, which is the negated direction to it.</summary>
    public Vector3 LightDirection { get; private set; } = -Vector3.UnitY;

    /// <summary>The shadow map's side, in texels.</summary>
    public int Resolution { get; private set; } = Shadows.DefaultResolution;

    /// <summary>One per cascade: the eye, the matrices, the extent and the texel size.</summary>
    public IReadOnlyList<CascadeFit> Cascades => _fits;

    /// <summary>One per cascade: the depth range it covers, in world units of camera view depth.</summary>
    public IReadOnlyList<CascadeRange> Ranges => _ranges;

    /// <summary>The camera's eight slice corners for each cascade, world space, laid out one cascade at a time.</summary>
    /// <remarks>
    /// Computed once per frame and handed to the depth pass so it can skip a caster that no cascade can see. Culling is
    /// not in <c>docs/shadows.md</c> §8's "what this is not" list for no reason: a shadow pass that draws every caster
    /// into every cascade costs five draws a frame where two would do.
    /// </remarks>
    public IReadOnlyList<Vector3> SliceCorners => _corners;

    /// <summary>No shadows: the value a backend without a depth pass draws with.</summary>
    public static ShadowFrame None { get; } = new();

    /// <summary>
    /// Gathers a frame. Returns <see cref="None"/> — with nothing fitted and nothing enabled — when the scene has no
    /// camera, no enabled directional light that casts, or a light whose <see cref="Light.Shadows"/> is
    /// <see cref="ShadowType.None"/>.
    /// </summary>
    /// <param name="gameObjects">The scene's objects, which is where the settings and the light are found.</param>
    /// <param name="camera">The camera being drawn through.</param>
    /// <param name="aspect">Viewport width over height.</param>
    /// <param name="resolution">
    /// The shadow map's side. Zero or fewer means "ask the light", which is its <see cref="Light.ShadowResolution"/>
    /// spelled in texels; an explicit number overrides it.
    /// </param>
    public static ShadowFrame Build(
        IEnumerable<GameObject>? gameObjects,
        Camera? camera,
        float aspect,
        int resolution = 0)
    {
        if (gameObjects is null || camera is null || camera.GameObject is null)
        {
            return None;
        }

        // Walked twice — once for the settings, once for the light — and a scene's collection is live enough for a
        // script to have added to it this frame, so it is read once into a list.
        List<GameObject> objects = [.. gameObjects];

        Shadows settings = Shadows.Resolve(objects);
        Light? light = FirstCastingLight(objects);

        if (light is null || light.GameObject is null)
        {
            return None;
        }

        int side = resolution > 0 ? resolution : Shadows.TexelsOf(light.ShadowResolution);

        // The camera's own world matrix, built the way `Camera.GetViewMatrix` builds its view but in the other
        // direction: the slice corners are defined in camera space and the fit needs them in the world's.
        Matrix4x4 cameraWorld = camera.GameObject.Transform.GetModelMatrix();
        CascadeRange[] ranges = settings.Ranges(camera.NearClipPlane);
        Vector3 directionToLight = LightSpaceFit.DirectionToLight(light.GameObject.Transform.Rotation);

        CascadeFit[] fits = new CascadeFit[ranges.Length];
        Vector3[] corners = new Vector3[ranges.Length * 8];

        for (int i = 0; i < ranges.Length; i++)
        {
            SliceGeometry geometry = new(aspect, camera.FieldOfViewRadians, ranges[i].Near, ranges[i].Far);

            fits[i] = LightSpaceFit.Fit(geometry, cameraWorld, directionToLight, side);

            Vector3[] sliceCorners = geometry.Corners();

            for (int corner = 0; corner < sliceCorners.Length; corner++)
            {
                corners[i * 8 + corner] = Vector3.Transform(sliceCorners[corner], cameraWorld);
            }
        }

        return new ShadowFrame
        {
            Enabled = true,
            Settings = settings,
            Light = light,
            LightDirection = -directionToLight,
            Resolution = side,
            _fits = fits,
            _ranges = ranges,
            _corners = corners
        };
    }

    /// <summary>
    /// The light the frame shades from, if it casts. The first enabled directional light on an active object — the same
    /// one <c>OpenGLSceneRenderer</c> picks with <c>FirstOrDefault</c>, so a scene with two directional lights still
    /// shades and shadows from the first and the second's setting is inert rather than half-honoured.
    /// </summary>
    private static Light? FirstCastingLight(IEnumerable<GameObject> gameObjects)
    {
        foreach (GameObject gameObject in gameObjects)
        {
            if (!gameObject.ActiveInHierarchy)
            {
                continue;
            }

            foreach (Light light in gameObject.GetComponents<Light>())
            {
                if (light.Enabled && light.Type == LightType.Directional)
                {
                    return light.Shadows == ShadowType.None ? null : light;
                }
            }
        }

        return null;
    }
}
