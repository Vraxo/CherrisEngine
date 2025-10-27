namespace Cherris;

public class MeshRenderer : Component, IDisposable
{
    public Mesh Mesh { get; }
    public string MeshName { get; }

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

    // This property will hold backend-specific data (e.g., Veldrid resource sets, buffers)
    public object BackendData { get; set; }

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