using Cherris.Attributes;
using Cherris.Rendering;
using System.Numerics;

namespace Cherris.Core;

public class Material
{
    [DragDropTarget("ASSET_PATH_TEXTURE")]
    public string TextureName { get; set; }

    public ITexture Texture { get; set; }
    public Vector2 TextureTiling { get; set; } = Vector2.One;

    [ColorUsage]
    public Vector3 EmissiveColor { get; set; } = Vector3.Zero;

    [Range(0f, 1f, 0.01f)]
    public float SpecularIntensity { get; set; } = 0.5f;

    [Range(1f, 128f, 1f)]
    public float Shininess { get; set; } = 32.0f;

    public Material(ITexture texture, string textureName)
    {
        Texture = texture;
        TextureName = textureName;
    }
}