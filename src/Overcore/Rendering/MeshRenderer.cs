using Overcore.Attributes;
using Overcore.Core;
using Overcore.Utils;

namespace Overcore.Components;

/// <summary>What a mesh does in the shadow pass, and whether the colour pass draws it at all.</summary>
public enum ShadowCastingMode
{
    /// <summary>Never drawn into a shadow map. The object is lit but casts nothing.</summary>
    Off,

    /// <summary>
    /// Drawn into the depth map with the face toward the light culled — the depth pass culls front faces, so the map
    /// records the surface the light cannot reach. The cheap and ordinary case.
    /// </summary>
    On,

    /// <summary>Drawn with no face culling, for a foliage card or a wall with two sides.</summary>
    TwoSided,

    /// <summary>Drawn into shadow maps and skipped in the colour pass: an invisible caster.</summary>
    ShadowsOnly
}

public class MeshRenderer : Component, IDisposable
{
    public required Mesh Mesh { get; set; }

    [DragDropTarget("ASSET_PATH_MESH")]
    public required string MeshName { get; set; }

    public Material Material
    {
        get;

        set
        {
            if ((field) == value)
            {
                return;
            }

            field = value;
            InvalidateBackendData();
        }
    }

    [HideInInspector]
    public object? BackendData { get; set; }

    /// <summary>
    /// The skinning backend's state: the bone texture and the palette buffer, which a static mesh has no use for.
    /// Kept apart from <see cref="BackendData"/> so a mesh that gains or loses a skeleton does not throw away the
    /// vertex buffers it shares between both paths.
    /// </summary>
    [HideInInspector]
    public object? SkinnedBackendData { get; set; }

    /// <summary>What this mesh does in the shadow pass. <see cref="ShadowCastingMode.ShadowsOnly"/> is the invisible
    /// caster: a collider-shaped box standing in for a detailed prop, with no colour draw of its own.</summary>
    public ShadowCastingMode CastShadows { get; set; } = ShadowCastingMode.On;

    /// <summary>Whether the shader samples shadows at all. Off is what makes a fake shadow blob or a baked look
    /// possible, and it is per renderer rather than per light for exactly that reason.</summary>
    public bool ReceiveShadows { get; set; } = true;

    private void InvalidateBackendData()
    {
        (BackendData as IDisposable)?.Dispose();
        (SkinnedBackendData as IDisposable)?.Dispose();

        BackendData = null;
        SkinnedBackendData = null;
    }

    public void Dispose()
    {
        InvalidateBackendData();
    }

    /// <summary>
    /// A clone drops the backend handles it inherited. <see cref="PendingDestruction.CloneComponent"/> is a shallow
    /// copy, so an instantiated object's renderer starts out holding the original's VAO and buffers; once the original
    /// is destroyed those are gone, and the clone would draw from freed handles. Forgetting them here makes the
    /// renderer build its own on the next frame, which is also what keeps two instances of one mesh from freeing each
    /// other's GPU state.
    /// </summary>
    internal override void OnInstantiated()
    {
        base.OnInstantiated();

        // Not InvalidateBackendData: that would dispose the handles the *original* is still drawing with.
        BackendData = null;
        SkinnedBackendData = null;
    }
}