using System;
using System.Numerics;
using Cherris.Rendering;

namespace Cherris;

public class MeshRenderer : Component, IDisposable
{
    public Mesh Mesh { get; }
    public string MeshName { get; }

    private ITexture _texture;
    public ITexture Texture
    {
        get => _texture;
        set
        {
            if (_texture != value)
            {
                _texture = value;
                InvalidateBackendData();
            }
        }
    }
    public string TextureName { get; set; }

    // MODIFIED: Added setters to allow editing from the inspector
    public Vector2 TextureTiling { get; set; }
    public Vector3 EmissiveColor { get; set; }

    // This property will hold backend-specific data (e.g., Veldrid resource sets, buffers)
    public object BackendData { get; set; }

    public MeshRenderer(Mesh mesh, ITexture texture, Vector2 textureTiling, Vector3 emissiveColor, string textureName, string meshName)
    {
        Mesh = mesh;
        MeshName = meshName;
        _texture = texture; // Set backing field directly to avoid invalidation in constructor
        TextureName = textureName;
        TextureTiling = textureTiling;
        EmissiveColor = emissiveColor;
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