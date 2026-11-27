using System.Numerics;

namespace Overcore.Components;

/// <summary>
/// A screen-space rectangle positioned by normalized anchors, pixel offsets, size and pivot. Anchor Y coordinates
/// are measured from the top of the parent, matching pointer coordinates and the engine's top-left screen origin.
/// </summary>
public sealed class RectTransform : Component
{
    /// <summary>Lower/upper anchor point in the parent's normalized rectangle.</summary>
    public Vector2 AnchorMin { get; set; } = new(0.5f, 0.5f);
    public Vector2 AnchorMax { get; set; } = new(0.5f, 0.5f);

    /// <summary>Pixel offset from the anchor reference point.</summary>
    public Vector2 AnchoredPosition { get; set; }

    /// <summary>Additional size in pixels beyond the distance between the anchors.</summary>
    public Vector2 SizeDelta { get; set; } = new(100f, 100f);

    /// <summary>Normalized point within this rectangle that sits on its anchored position.</summary>
    public Vector2 Pivot { get; set; } = new(0.5f, 0.5f);

    /// <summary>Resolves this rectangle against a parent rectangle in top-left-origin pixel coordinates.</summary>
    public RectF Resolve(RectF parent)
    {
        Vector2 anchorMin = Vector2.Clamp(AnchorMin, Vector2.Zero, Vector2.One);
        Vector2 anchorMax = Vector2.Clamp(AnchorMax, anchorMin, Vector2.One);
        Vector2 pivot = Vector2.Clamp(Pivot, Vector2.Zero, Vector2.One);

        float width = MathF.Max(0f, parent.Width * (anchorMax.X - anchorMin.X) + SizeDelta.X);
        float height = MathF.Max(0f, parent.Height * (anchorMax.Y - anchorMin.Y) + SizeDelta.Y);
        float anchorX = parent.X + parent.Width * (anchorMin.X + (anchorMax.X - anchorMin.X) * pivot.X);
        float anchorY = parent.Y + parent.Height * (anchorMin.Y + (anchorMax.Y - anchorMin.Y) * pivot.Y);
        float x = anchorX + AnchoredPosition.X - width * pivot.X;
        float y = anchorY + AnchoredPosition.Y - height * pivot.Y;

        return new RectF(x, y, width, height);
    }
}

/// <summary>A top-left-origin rectangle measured in pixels.</summary>
public readonly record struct RectF(float X, float Y, float Width, float Height)
{
    public float Right => X + Width;
    public float Bottom => Y + Height;
    public bool IsEmpty => Width <= 0f || Height <= 0f;

    public RectF Intersect(RectF other)
    {
        float left = MathF.Max(X, other.X);
        float top = MathF.Max(Y, other.Y);
        float right = MathF.Min(Right, other.Right);
        float bottom = MathF.Min(Bottom, other.Bottom);
        return new RectF(left, top, MathF.Max(0f, right - left), MathF.Max(0f, bottom - top));
    }

    public bool Contains(Vector2 point) =>
        !IsEmpty && point.X >= X && point.X <= Right && point.Y >= Y && point.Y <= Bottom;
}
