using System.Numerics;

namespace Overcore.Components;

/// <summary>Screen-space text rendered from the runtime host's built-in font atlas.</summary>
public sealed class Text : Component
{
    public string Content { get; set; } = "Text";

    /// <summary>Project-relative TrueType font asset path; blank selects the engine's bundled font.</summary>
    public string FontName { get; set; } = String.Empty;

    public float FontSize { get; set; } = 24f;
    public Vector3 Color { get; set; } = Vector3.One;
    public float Alpha { get; set; } = 1f;
    public bool RaycastTarget { get; set; } = true;
    public HorizontalTextAlignment HorizontalAlignment { get; set; } = HorizontalTextAlignment.Left;
    public VerticalTextAlignment VerticalAlignment { get; set; } = VerticalTextAlignment.Top;

    /// <summary>Additional pixels between baselines, before Canvas scaling.</summary>
    public float LineSpacing { get; set; }

    /// <summary>Additional pixels between glyph advances, before Canvas scaling.</summary>
    public float CharacterSpacing { get; set; }

    /// <summary>Wrap lines to the element's width. InputField text remains single-line.</summary>
    public bool Wrap { get; set; } = true;
}

public enum HorizontalTextAlignment
{
    Left,
    Center,
    Right
}

public enum VerticalTextAlignment
{
    Top,
    Middle,
    Bottom
}
