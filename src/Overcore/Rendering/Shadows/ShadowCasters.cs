using Overcore.Components;
using Overcore.Core;

namespace Overcore.Rendering.Shadows;

/// <summary>What a shadow pass does with one object, decided without a GPU so a test can pin it.</summary>
public enum CasterRole
{
    /// <summary>Drawn into the depth map only: <see cref="ShadowCastingMode.ShadowsOnly"/>.</summary>
    Caster,

    /// <summary>Drawn into the depth map and in the colour pass.</summary>
    CasterAndVisible,

    /// <summary>In the colour pass only, so it receives a shadow without casting one.</summary>
    NotACaster
}

/// <summary>
/// Which objects a shadow pass draws, and which side of the culling it takes them from.
/// </summary>
/// <remarks>
/// <para>
/// Kept out of the draw loop and out of the renderer because it is a decision, not a draw call: every rule below is the
/// kind that looks obviously right in a loop and is wrong in one of six ways. A test can pin all six; a green frame
/// cannot pin any of them.
/// </para>
/// <para>
/// The exclusion that is easiest to lose in a fourth copy of the draw loop is the <see cref="Skybox"/>: the sky is a
/// source of light, not an occluder, and a depth map that contains it is a depth map with nothing in the world outside
/// the sky's silhouette.
/// </para>
/// </remarks>
public static class ShadowCasters
{
    /// <summary>
    /// What the shadow pass does with <paramref name="gameObject"/>: nothing, draw it in depth only, or draw it in both
    /// passes. A renderer with no mesh, a disabled renderer, an inactive object and a skybox are all
    /// <see cref="CasterRole.NotACaster"/>.
    /// </summary>
    public static CasterRole RoleOf(GameObject gameObject)
    {
        if (gameObject is null || !gameObject.ActiveInHierarchy)
        {
            return CasterRole.NotACaster;
        }

        MeshRenderer? renderer = gameObject.GetComponent<MeshRenderer>();

        if (renderer is null || renderer.Mesh is null || !renderer.Enabled)
        {
            return CasterRole.NotACaster;
        }

        // The sky is lit by the scene, not by the scene's shadows, and it occludes nothing.
        if (gameObject.GetComponent<Skybox>() is not null)
        {
            return CasterRole.NotACaster;
        }

        return renderer.CastShadows switch
        {
            ShadowCastingMode.Off => CasterRole.NotACaster,
            ShadowCastingMode.ShadowsOnly => CasterRole.Caster,
            _ => CasterRole.CasterAndVisible
        };
    }

    /// <summary>Whether the depth pass draws this object: everything but <see cref="CasterRole.NotACaster"/>.</summary>
    public static bool IsCaster(GameObject gameObject) => RoleOf(gameObject) != CasterRole.NotACaster;

    /// <summary>
    /// Whether the colour pass draws this object. Everything except a <see cref="ShadowCastingMode.ShadowsOnly"/>
    /// caster, which is the whole point of that mode.
    /// </summary>
    public static bool IsVisible(GameObject gameObject) => RoleOf(gameObject) != CasterRole.Caster;

    /// <summary>
    /// Whether the depth pass may cull back faces for this object. Only <see cref="ShadowCastingMode.TwoSided"/> turns
    /// culling off, because a foliage card or a two-sided wall has no back face to cull to.
    /// </summary>
    public static bool CullsBackFaces(GameObject gameObject)
    {
        return gameObject?.GetComponent<MeshRenderer>()?.CastShadows != ShadowCastingMode.TwoSided;
    }

    /// <summary>The objects the depth pass draws, in scene order.</summary>
    public static IEnumerable<GameObject> Collect(IEnumerable<GameObject>? gameObjects)
    {
        if (gameObjects is null)
        {
            yield break;
        }

        foreach (GameObject gameObject in gameObjects)
        {
            if (IsCaster(gameObject))
            {
                yield return gameObject;
            }
        }
    }
}
