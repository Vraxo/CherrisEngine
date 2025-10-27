using Cherris.Rendering;
using System.Numerics;

namespace Cherris;

public class Material
{
    public ITexture Texture { get; set; }
    public string TextureName { get; set; }
    public Vector2 TextureTiling { get; set; } = Vector2.One;
    public Vector3 EmissiveColor { get; set; } = Vector3.Zero;
    public float SpecularIntensity { get; set; } = 0.5f;
    public float Shininess { get; set; } = 32.0f;

    public Material(ITexture texture, string textureName)
    {
        Texture = texture;
        TextureName = textureName;
    }
}