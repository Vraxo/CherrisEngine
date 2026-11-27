using Overcore.Attributes;
using System.Numerics;

namespace Overcore.Components;

/// <summary>A textured or solid-color graphic, rendered as a screen-space rectangle.</summary>
public sealed class Image : Component
{
    /// <summary>Project-relative texture asset to draw; blank uses a solid-color rectangle.</summary>
    [DragDropTarget("ASSET_PATH_SPRITE")]
    public string SpriteName { get; set; } = String.Empty;

    /// <summary>Direct project-relative texture fallback; a valid SpriteName supplies its source texture and UVs.</summary>
    [DragDropTarget("ASSET_PATH_TEXTURE")]
    public string TextureName { get; set; } = String.Empty;

    /// <summary>Bottom-left UV of the displayed region, used for sprite-atlas cropping.</summary>
    public Vector2 UVMin { get; set; } = Vector2.Zero;

    /// <summary>Top-right UV of the displayed region, used for sprite-atlas cropping.</summary>
    public Vector2 UVMax { get; set; } = Vector2.One;

    /// <summary>Fractional sprite-border metadata for nine-sliced images, measured from the source region.</summary>
    public float SliceLeft { get; set; }
    public float SliceTop { get; set; }
    public float SliceRight { get; set; }
    public float SliceBottom { get; set; }

    /// <summary>Linear RGB tint multiplied by the source texture or solid fill.</summary>
    public Vector3 Color { get; set; } = Vector3.One;

    public float Alpha { get; set; } = 1f;

    /// <summary>When false, this image does not participate in pointer hit-testing.</summary>
    public bool RaycastTarget { get; set; } = true;
}
