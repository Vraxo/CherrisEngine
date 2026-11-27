namespace Overcore.Components;

/// <summary>Clips descendant UI graphics to this object's resolved rectangle.</summary>
/// <remarks>
/// Descendant graphics and pointer hit-tests are clipped to the mask shape. With zero corner radius and feather, the
/// renderer uses its scissor test; rounded or feathered masks additionally clip generated triangles. Feathering is
/// applied in the fragment shader. An optional alpha texture is stretched across the mask rectangle. The mask does not
/// affect this object's own graphic.
/// </remarks>
public sealed class RectMask2D : Component
{
    /// <summary>Corner radius in the mask object's logical Canvas units. Zero keeps the existing rectangular mask.</summary>
    public float CornerRadius { get; set; }

    /// <summary>Inside-edge fade width in logical Canvas units. Zero produces a crisp edge.</summary>
    public float Feather { get; set; }

    /// <summary>Optional project texture whose alpha channel masks the descendants across this rectangle.</summary>
    public string MaskTextureName { get; set; } = String.Empty;

    /// <summary>Alpha threshold in the range zero to one used by pointer hit-testing.</summary>
    public float MaskThreshold { get; set; } = 0.5f;
}
