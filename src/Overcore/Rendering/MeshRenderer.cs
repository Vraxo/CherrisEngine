using Overcore.Attributes;
using Overcore.Core;
using Overcore.Utils;

namespace Overcore.Components;

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