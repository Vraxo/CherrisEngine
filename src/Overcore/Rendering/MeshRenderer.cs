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