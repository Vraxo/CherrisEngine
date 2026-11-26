using Overcore.Attributes;
using Overcore.Rendering;
using System.Numerics;

namespace Overcore.Core;

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

    /// <summary>
    /// Alpha below this is thrown away rather than drawn, i.e. a cutout material: a chain-link fence, a leaf card, a
    /// grille. <c>0</c> turns the test off, which is the default and is what every material written before this
    /// property existed keeps doing.
    /// </summary>
    /// <remarks>
    /// <para>
    /// A cutoff and not a transparency: there is no blending anywhere in this renderer's scene pass, so the only two
    /// things a fragment's alpha could mean are "draw it" or "do not". Unity calls the same setting
    /// <c>_Cutoff</c> with the same two jobs — the colour pass discards and the shadow pass discards — and keeping
    /// both on one value is what stops a fence from being a fence in the light and a solid wall in its shadow.
    /// </para>
    /// <para>
    /// It lives on the material rather than on the renderer because whether a texture has holes is a property of the
    /// texture, and a shadow-casting quad that shares an atlas with an opaque one should not need its own setting.
    /// </para>
    /// </remarks>
    [Range(0f, 1f, 0.01f)]
    public float AlphaCutoff { get; set; }

    public Material(ITexture texture, string textureName)
    {
        Texture = texture;
        TextureName = textureName;
    }
}