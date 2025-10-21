using System;
using System.Numerics;
using Cherris.Rendering;

namespace Cherris;

public class MeshRenderer : Component, IDisposable
{
    public Mesh Mesh { get; }
    public ITexture Texture { get; }
    public Vector2 TextureTiling { get; }
    public Vector3 EmissiveColor { get; }

    // This property will hold backend-specific data (e.g., Veldrid resource sets, buffers)
    public object BackendData { get; set; }

    public MeshRenderer(Mesh mesh, ITexture texture, Vector2 textureTiling, Vector3 emissiveColor)
    {
        Mesh = mesh;
        Texture = texture;
        TextureTiling = textureTiling;
        EmissiveColor = emissiveColor;
    }

    public void Dispose()
    {
        (BackendData as IDisposable)?.Dispose();
        BackendData = null;
    }
}