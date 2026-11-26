using Overcore.Attributes;
using Overcore.Components;
using Overcore.Utils;

namespace Overcore.Rendering.Shadows;

/// <summary>
/// The per-scene shadow settings: how far shadows reach, how many cascades cover that distance, and how much the
/// machine is asked to spend on them.
/// </summary>
/// <remarks>
/// <para>
/// A component on one object rather than a root-level block in the scene file, and the loader forces that rather than
/// taste: <c>SceneLoader</c> parses a scene as <c>Dictionary&lt;string, List&lt;...&gt;&gt;</c> and looks up exactly
/// <c>GameObjects</c>, so a sibling mapping would fail the whole deserialize. A component needs one factory
/// registration and nothing else.
/// </para>
/// <para>
/// A scene with no <see cref="Shadows"/> object still gets these defaults, so nothing has to be added to an existing
/// project for it to have shadows — adding the component is how a scene <em>departs</em> from them.
/// </para>
/// <para>
/// Exactly one is consulted: the first on an object that is active in the hierarchy with the component enabled, which
/// is the rule <c>Scene.FindMainComponents</c> already applies to <see cref="Camera"/> and <see cref="Skybox"/>.
/// </para>
/// </remarks>
public class Shadows : Component
{
    private float[] _splits = CascadeSplits.Derive(CascadeSplits.DefaultCascades);
    private int _cascades = CascadeSplits.DefaultCascades;

    /// <summary>
    /// Metres of camera view depth covered by shadows. Past it nothing is shadowed, so it should reach beyond where the
    /// eye is judging contact.
    /// </summary>
    [Range(CascadeSplits.MinimumDistance, CascadeSplits.MaximumDistance, 1f)]
    public float Distance { get; set; } = CascadeSplits.DefaultDistance;

    /// <summary>
    /// How many shadow maps cover that distance. One, two or four; three rounds to four, because three divides a view
    /// into one wide cascade and one narrow one and buys nothing.
    /// </summary>
    /// <remarks>
    /// Setting this rewrites <see cref="Splits"/> in place to the derived defaults, which is what lets a fresh instance
    /// compare equal to a loaded one: a stored list whose default depended on a mutable property could never
    /// round-trip through a serializer that writes only what differs from a new instance.
    /// </remarks>
    [Range(1, CascadeSplits.MaximumCascades, 1f)]
    public int Cascades
    {
        get => _cascades;
        set
        {
            int cascades = CascadeSplits.SupportedCascades(value);

            if (cascades == _cascades)
            {
                return;
            }

            _cascades = cascades;
            _splits = CascadeSplits.Derive(cascades);
        }
    }

    /// <summary>
    /// The cumulative boundaries between cascades as fractions of <see cref="Distance"/>, or the derived ones. Written
    /// to the scene file only once a hand edit has made it differ — see <see cref="CascadeSplits.Sanitize"/> for how a
    /// list that disagrees with <see cref="Cascades"/> is treated on load.
    /// </summary>
    [HideInInspector]
    public float[] Splits
    {
        get => _splits;
        set => _splits = value ?? CascadeSplits.Derive(_cascades);
    }


    /// <summary>The default shadow-map size a scene gets, in texels on a side, for a light that does not set its own.
    /// 2048 is Unity's default quality level and the size the contract in <c>docs/shadows.md</c> §5.1 is written for.
    /// </summary>
    public const int DefaultResolution = 2048;

    /// <summary>
    /// How wide the cross-fade between two cascades is, as a fraction of the cascade's own depth range. A tenth is
    /// Unity's default and is set here rather than exposed because a value of zero reintroduces the hard seam the
    /// blend exists to remove, and no scene has yet wanted the seam back.
    /// </summary>
    public const float BlendBand = 0.1f;

    /// <summary>
    /// The PCF tent's radius in texels. One is a 3x3 tap pattern: wide enough that an edge reads as soft, narrow
    /// enough that a contact point stays where it is. Not exposed for the same reason as <see cref="BlendBand"/>.
    /// </summary>
    public const float PcfTexels = 1f;

    /// <summary>Texels on a side for a <see cref="ShadowResolution"/> level.</summary>
    /// <param name="resolution">The light's setting.</param>
    public static int TexelsOf(ShadowResolution resolution) => resolution switch
    {
        ShadowResolution.Low => 512,
        ShadowResolution.Medium => 1024,
        ShadowResolution.VeryHigh => 4096,
        _ => DefaultResolution
    };

    /// <summary>The world units one texel covers in the innermost cascade, at the defaults. The receiver's normal
    /// offset is in these, so the number is worth being able to read rather than guess at.</summary>
    /// <param name="cameraNearPlane">The camera's near plane.</param>
    /// <param name="aspect">Viewport width over height.</param>
    /// <param name="fieldOfViewRadians">The camera's vertical field of view.</param>
    /// <param name="resolution">The shadow map's side, in texels.</param>
    public float InnerTexelSize(
        float cameraNearPlane,
        float aspect,
        float fieldOfViewRadians,
        int resolution = DefaultResolution)
    {
        CascadeRange[] ranges = Ranges(cameraNearPlane);
        SliceGeometry geometry = new(aspect, fieldOfViewRadians, ranges[0].Near, ranges[0].Far);

        // The slice's own scale is all the texel depends on, so an identity camera is enough to measure it.
        return LightSpaceFit.Fit(geometry, System.Numerics.Matrix4x4.Identity, System.Numerics.Vector3.UnitY, resolution)
            .TexelSize;
    }

    /// <summary>Where each cascade ends, in metres of camera view depth. The renderer's input to the fit.</summary>
    public float[] FarDepths() => CascadeSplits.FarDepths(Distance, Cascades, _splits);

    /// <summary>The cascades themselves, with any unusable split list already repaired.</summary>
    public CascadeRange[] Ranges(float cameraNearPlane) =>
        CascadeSplits.Ranges(Distance, cameraNearPlane, Cascades, _splits);

    /// <summary>
    /// The settings in force this frame: the first enabled <see cref="Shadows"/> on an active object, or a detached
    /// instance carrying the defaults when the scene has none.
    /// </summary>
    /// <param name="gameObjects">The scene's objects.</param>
    /// <remarks>
    /// The fallback is a fresh component rather than a cached one so a caller can never mutate the defaults for
    /// everybody. It is read-only in practice: the renderer only ever asks it for numbers.
    /// </remarks>
    public static Shadows Resolve(IEnumerable<Overcore.Core.GameObject>? gameObjects)
    {
        if (gameObjects is not null)
        {
            foreach (Overcore.Core.GameObject gameObject in gameObjects)
            {
                if (!gameObject.ActiveInHierarchy)
                {
                    continue;
                }

                foreach (Shadows candidate in gameObject.GetComponents<Shadows>())
                {
                    if (candidate.Enabled)
                    {
                        return candidate;
                    }
                }
            }
        }

        return new Shadows();
    }
}
