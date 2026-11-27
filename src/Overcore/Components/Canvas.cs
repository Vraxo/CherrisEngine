namespace Overcore.Components;

/// <summary>Root and ordering settings for a screen-space UI tree.</summary>
public sealed class Canvas : Component
{
    /// <summary>Reference width used by <see cref="CanvasScaleMode.ScaleWithScreenSize"/>.</summary>
    public float ReferenceWidth { get; set; } = 1920f;

    /// <summary>Reference height used by <see cref="CanvasScaleMode.ScaleWithScreenSize"/>.</summary>
    public float ReferenceHeight { get; set; } = 1080f;

    /// <summary>Blend between width- and height-based scaling. Zero follows width; one follows height.</summary>
    public float MatchWidthOrHeight { get; set; } = 0.5f;

    public CanvasScaleMode ScaleMode { get; set; } = CanvasScaleMode.ScaleWithScreenSize;

    /// <summary>Higher values are drawn later and receive pointer hits before lower canvases.</summary>
    public int SortingOrder { get; set; }

    public float GetScale(float width, float height)
    {
        if (ScaleMode == CanvasScaleMode.ConstantPixelSize)
        {
            return 1f;
        }

        float referenceWidth = MathF.Max(1f, ReferenceWidth);
        float referenceHeight = MathF.Max(1f, ReferenceHeight);
        float widthScale = MathF.Max(1f, width) / referenceWidth;
        float heightScale = MathF.Max(1f, height) / referenceHeight;
        float match = Math.Clamp(MatchWidthOrHeight, 0f, 1f);
        return MathF.Pow(widthScale, 1f - match) * MathF.Pow(heightScale, match);
    }
}

public enum CanvasScaleMode
{
    ConstantPixelSize,
    ScaleWithScreenSize
}
