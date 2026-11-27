namespace Overcore.Components;

/// <summary>Common sizing, spacing, padding, and alignment settings for a UI layout container.</summary>
public abstract class LayoutGroup : Component
{
    /// <summary>Space between adjacent children along the group's primary axis, in logical canvas pixels.</summary>
    public float Spacing { get; set; }

    public float PaddingLeft { get; set; }
    public float PaddingRight { get; set; }
    public float PaddingTop { get; set; }
    public float PaddingBottom { get; set; }

    /// <summary>Alignment of the packed children along both the primary and cross axes.</summary>
    public LayoutAlignment ChildAlignment { get; set; } = LayoutAlignment.Start;

    /// <summary>Whether the group controls child width/height instead of preserving the resolved RectTransform size.</summary>
    public bool ControlChildWidth { get; set; } = true;
    public bool ControlChildHeight { get; set; } = true;

    /// <summary>Whether unused space is shared equally between children along each axis.</summary>
    public bool ChildForceExpandWidth { get; set; }
    public bool ChildForceExpandHeight { get; set; }
}

/// <summary>Arranges direct children from left to right.</summary>
public sealed class HorizontalLayoutGroup : LayoutGroup
{
}

/// <summary>Arranges direct children from top to bottom.</summary>
public sealed class VerticalLayoutGroup : LayoutGroup
{
}

public enum LayoutAlignment
{
    Start,
    Center,
    End
}
