using Overcore.Components;
using Overcore.Core;
using Overcore.Utils;
using System.Collections.Concurrent;
using System.Numerics;

namespace Overcore.Rendering.UI;

/// <summary>A convex, pixel-space rounded rectangle used to clip descendant UI graphics and input.</summary>
public sealed class RoundedRectClip
{
    private const int SegmentsPerCorner = 6;
    private sealed record AlphaMask(int Width, int Height, byte[] Pixels);
    private sealed record AlphaMaskCacheEntry(AlphaMask? Mask);
    private static readonly ConcurrentDictionary<string, AlphaMaskCacheEntry> s_alphaMasks = new(StringComparer.OrdinalIgnoreCase);
    private static readonly object s_alphaMaskGate = new();
    private readonly Vector2[] _polygon;
    private readonly AlphaMask? _alphaMask;

    public RectF Rect { get; }
    public float Radius { get; }
    public float Feather { get; }
    public string MaskTextureName { get; }
    public float MaskThreshold { get; }
    internal Vector2[] Polygon => _polygon;

    public RoundedRectClip(RectF rect, float radius, float feather = 0f,
        string? maskTextureName = null, float maskThreshold = 0.5f)
    {
        Rect = rect;
        float maxRadius = rect.IsEmpty || !float.IsFinite(rect.Width) || !float.IsFinite(rect.Height)
            ? 0f
            : MathF.Max(0f, MathF.Min(rect.Width, rect.Height) * 0.5f);
        Radius = Math.Clamp(float.IsFinite(radius) ? radius : 0f, 0f, maxRadius);
        float maxFeather = float.IsFinite(rect.Width) && float.IsFinite(rect.Height)
            ? MathF.Max(0f, MathF.Max(rect.Width, rect.Height))
            : 0f;
        Feather = Math.Clamp(float.IsFinite(feather) ? feather : 0f, 0f, maxFeather);
        string requestedMaskPath = (maskTextureName?.Trim() ?? String.Empty).Replace('\\', '/');
        MaskTextureName = IsProjectAssetPath(requestedMaskPath) ? requestedMaskPath : String.Empty;
        MaskThreshold = Math.Clamp(float.IsFinite(maskThreshold) ? maskThreshold : 0.5f, 0f, 1f);
        _polygon = CreatePolygon();
        _alphaMask = LoadAlphaMask(MaskTextureName);
    }

    public bool Contains(Vector2 point)
    {
        if (!Rect.Contains(point)) return false;
        if (Radius > 0f)
        {
            for (int index = 0; index < _polygon.Length; index++)
            {
                Vector2 edge = _polygon[(index + 1) % _polygon.Length] - _polygon[index];
                Vector2 relative = point - _polygon[index];
                if (edge.X * relative.Y - edge.Y * relative.X < -0.0001f) return false;
            }
        }

        if (_alphaMask is not { } alphaMask) return true;
        float u = Math.Clamp((point.X - Rect.X) / Rect.Width, 0f, 1f);
        float v = Math.Clamp((point.Y - Rect.Y) / Rect.Height, 0f, 1f);
        float sampleX = Math.Clamp(u * alphaMask.Width - 0.5f, 0f, alphaMask.Width - 1f);
        float sampleY = Math.Clamp(v * alphaMask.Height - 0.5f, 0f, alphaMask.Height - 1f);
        int x0 = (int)sampleX;
        int y0 = (int)sampleY;
        int x1 = Math.Min(x0 + 1, alphaMask.Width - 1);
        int y1 = Math.Min(y0 + 1, alphaMask.Height - 1);
        float xBlend = sampleX - x0;
        float yBlend = sampleY - y0;
        float topLeft = GetAlpha(x0, y0);
        float topRight = GetAlpha(x1, y0);
        float bottomLeft = GetAlpha(x0, y1);
        float bottomRight = GetAlpha(x1, y1);
        float topAlpha = topLeft + (topRight - topLeft) * xBlend;
        float bottomAlpha = bottomLeft + (bottomRight - bottomLeft) * xBlend;
        float sampleAlpha = topAlpha + (bottomAlpha - topAlpha) * yBlend;
        return sampleAlpha >= MaskThreshold;

        float GetAlpha(int x, int y)
        {
            int alphaIndex = checked((y * alphaMask.Width + x) * 4 + 3);
            return alphaMask.Pixels[alphaIndex] / 255f;
        }
    }

    /// <summary>Clears cached CPU alpha masks after project assets are reloaded.</summary>
    public static void ClearAlphaMaskCache() => s_alphaMasks.Clear();

    private static bool IsProjectAssetPath(string path)
    {
        if (String.IsNullOrWhiteSpace(path)) return false;
        if (!Path.IsPathRooted(path))
        {
            return !path.Replace('\\', '/').Split('/').Any(segment => segment == "..");
        }

        if (String.IsNullOrWhiteSpace(ProjectFiles.ProjectRoot)) return false;
        try
        {
            string root = Path.GetFullPath(ProjectFiles.ProjectRoot).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
            string fullPath = Path.GetFullPath(path);
            StringComparison comparison = OperatingSystem.IsWindows()
                ? StringComparison.OrdinalIgnoreCase
                : StringComparison.Ordinal;
            return fullPath.StartsWith(root, comparison) && File.Exists(fullPath);
        }
        catch (Exception)
        {
            return false;
        }
    }

    private static AlphaMask? LoadAlphaMask(string textureName)
    {
        if (String.IsNullOrWhiteSpace(textureName)) return null;
        string key = $"{ProjectFiles.ProjectRoot}|{textureName}";
        return s_alphaMasks.GetOrAdd(key, _ => new AlphaMaskCacheEntry(DecodeAlphaMask(textureName))).Mask;
    }

    private static AlphaMask? DecodeAlphaMask(string textureName)
    {
        lock (s_alphaMaskGate)
        {
            try
            {
                ImageData? image = ImageLoader.LoadFromFile(textureName, flipVertical: false);
                if (image is null || image.Value.Width <= 0 || image.Value.Height <= 0) return null;
                ImageData data = image.Value;
                long required = (long)data.Width * data.Height * 4;
                return required <= data.Data.Length ? new AlphaMask(data.Width, data.Height, data.Data) : null;
            }
            catch (Exception)
            {
                return null;
            }
        }
    }

    private Vector2[] CreatePolygon()
    {
        if (Radius <= 0f)
        {
            return [new Vector2(Rect.X, Rect.Y), new Vector2(Rect.Right, Rect.Y),
                new Vector2(Rect.Right, Rect.Bottom), new Vector2(Rect.X, Rect.Bottom)];
        }

        List<Vector2> points = new(SegmentsPerCorner * 4 + 4);
        AddArc(points, new Vector2(Rect.Right - Radius, Rect.Y + Radius), Radius, -90f, 0f);
        AddArc(points, new Vector2(Rect.Right - Radius, Rect.Bottom - Radius), Radius, 0f, 90f);
        AddArc(points, new Vector2(Rect.X + Radius, Rect.Bottom - Radius), Radius, 90f, 180f);
        AddArc(points, new Vector2(Rect.X + Radius, Rect.Y + Radius), Radius, 180f, 270f);
        return [.. points];
    }

    private static void AddArc(List<Vector2> points, Vector2 center, float radius, float startDegrees, float endDegrees)
    {
        for (int index = 0; index <= SegmentsPerCorner; index++)
        {
            float angle = (startDegrees + (endDegrees - startDegrees) * index / SegmentsPerCorner) * (MathF.PI / 180f);
            points.Add(center + new Vector2(MathF.Cos(angle), MathF.Sin(angle)) * radius);
        }
    }
}

/// <summary>Clips triangle-list geometry against one or more convex rounded-rectangle masks.</summary>
public static class UIClipGeometry
{
    public static void ClipTriangles<T>(List<T> vertices, IReadOnlyList<RoundedRectClip> clips,
        Func<T, Vector2> getPosition, Func<T, T, float, T> interpolate)
    {
        ArgumentNullException.ThrowIfNull(vertices);
        ArgumentNullException.ThrowIfNull(clips);
        ArgumentNullException.ThrowIfNull(getPosition);
        ArgumentNullException.ThrowIfNull(interpolate);
        if (clips.Count == 0 || vertices.Count == 0) return;

        Vector2[][] masks = clips.Select(clip => clip.Polygon).ToArray();
        List<T> clipped = new(vertices.Count);
        List<T> polygon = new(32);
        List<T> temporary = new(32);
        for (int index = 0; index + 2 < vertices.Count; index += 3)
        {
            polygon.Clear();
            temporary.Clear();
            polygon.Add(vertices[index]);
            polygon.Add(vertices[index + 1]);
            polygon.Add(vertices[index + 2]);
            foreach (Vector2[] mask in masks)
            {
                for (int edgeIndex = 0; edgeIndex < mask.Length && polygon.Count > 0; edgeIndex++)
                {
                    ClipAgainstEdge(polygon, temporary, mask[edgeIndex], mask[(edgeIndex + 1) % mask.Length],
                        getPosition, interpolate);
                    (polygon, temporary) = (temporary, polygon);
                }
            }

            for (int vertexIndex = 1; vertexIndex + 1 < polygon.Count; vertexIndex++)
            {
                clipped.Add(polygon[0]);
                clipped.Add(polygon[vertexIndex]);
                clipped.Add(polygon[vertexIndex + 1]);
            }
        }

        vertices.Clear();
        vertices.AddRange(clipped);
    }

    private static void ClipAgainstEdge<T>(List<T> polygon, List<T> result, Vector2 edgeStart, Vector2 edgeEnd,
        Func<T, Vector2> getPosition, Func<T, T, float, T> interpolate)
    {
        result.Clear();
        if (polygon.Count == 0) return;
        T previous = polygon[^1];
        float previousDistance = Cross(edgeEnd - edgeStart, getPosition(previous) - edgeStart);
        bool previousInside = previousDistance >= -0.0001f;

        foreach (T current in polygon)
        {
            Vector2 currentPosition = getPosition(current);
            float currentDistance = Cross(edgeEnd - edgeStart, currentPosition - edgeStart);
            bool currentInside = currentDistance >= -0.0001f;
            if (currentInside != previousInside)
            {
                float denominator = previousDistance - currentDistance;
                float amount = MathF.Abs(denominator) < 1e-7f ? 0f : previousDistance / denominator;
                result.Add(interpolate(previous, current, Math.Clamp(amount, 0f, 1f)));
            }
            if (currentInside) result.Add(current);
            previous = current;
            previousDistance = currentDistance;
            previousInside = currentInside;
        }
    }

    private static float Cross(Vector2 left, Vector2 right) => left.X * right.Y - left.Y * right.X;
}
