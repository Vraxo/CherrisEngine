using Cherris.Attributes;
using Cherris.Core;
using Cherris.Utils;

namespace Cherris.Components;

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

    private void InvalidateBackendData()
    {
        (BackendData as IDisposable)?.Dispose();
        BackendData = null;
    }

    public void Dispose()
    {
        InvalidateBackendData();
    }
}