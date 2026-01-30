using Cherris.Attributes;
using Cherris.Core;

namespace Cherris.Components;

public class MeshRenderer : Component, IDisposable
{
    public Mesh Mesh { get; set; }

    [DragDropTarget("ASSET_PATH_MESH")]
    public string MeshName { get; set; }

    private Material _material;
    public Material Material
    {
        get => _material;
        set
        {
            if (_material != value)
            {
                _material = value;
                InvalidateBackendData();
            }
        }
    }

    [HideInInspector]
    public object? BackendData { get; set; }

    public MeshRenderer(Mesh mesh, Material material, string meshName)
    {
        Mesh = mesh;
        MeshName = meshName;
        _material = material;
    }

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