using Overcore.Components;
using Overcore.Core.Logging;
using Overcore.Utils;
using System.Numerics;
using System.Text.Json;

namespace Overcore.Rendering;

/// <summary>Pixel-space rectangle in a source texture.</summary>
public readonly record struct SpritePixelRect(int X, int Y, int Width, int Height);

/// <summary>Project-owned sprite import data, independent of any Unity asset format.</summary>
public readonly record struct ResolvedSprite(string TextureName, Vector2 UVMin, Vector2 UVMax,
    float SliceLeft, float SliceTop, float SliceRight, float SliceBottom);

public sealed record SpriteMetadata(
    string TextureName,
    int TextureWidth,
    int TextureHeight,
    SpritePixelRect Rect,
    Vector2 Pivot,
    float PixelsPerUnit,
    Vector4 Border)
{
    public Vector2 UVMin => new((float)Rect.X / TextureWidth,
        1f - (float)(Rect.Y + Rect.Height) / TextureHeight);
    public Vector2 UVMax => new((float)(Rect.X + Rect.Width) / TextureWidth,
        1f - (float)Rect.Y / TextureHeight);
    public float SliceLeft => Border.X / Rect.Width;
    public float SliceTop => Border.Y / Rect.Height;
    public float SliceRight => Border.Z / Rect.Width;
    public float SliceBottom => Border.W / Rect.Height;
}

/// <summary>Loads project-authored .sprite.json files and caches validated sprite metadata.</summary>
public static class SpriteAssetLoader
{
    private static readonly object s_gate = new();
    private static readonly Dictionary<string, SpriteMetadata?> s_cache = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Loads metadata by project-relative path; the extension may be omitted.</summary>
    public static SpriteMetadata? Load(string? spriteName)
    {
        if (String.IsNullOrWhiteSpace(spriteName)) return null;
        string path = Path.HasExtension(spriteName) ? spriteName : spriteName + ".sprite.json";
        string key = $"{ProjectFiles.ProjectRoot}|{path}";
        lock (s_gate)
        {
            if (s_cache.TryGetValue(key, out SpriteMetadata? cached)) return cached;
            using Stream? stream = ProjectFiles.Open(path);
            SpriteMetadata? metadata = stream is null ? null : Parse(stream);
            s_cache[key] = metadata;
            if (metadata is null) Logger.Warning($"[Sprite] Could not load sprite metadata '{path}'.");
            return metadata;
        }
    }

    /// <summary>Uses sidecar metadata when available, otherwise returns the Image's direct texture and UV fields.</summary>
    public static ResolvedSprite Resolve(Image image)
    {
        ArgumentNullException.ThrowIfNull(image);
        SpriteMetadata? metadata = Load(image.SpriteName);
        return metadata is null
            ? new ResolvedSprite(image.TextureName, image.UVMin, image.UVMax,
                image.SliceLeft, image.SliceTop, image.SliceRight, image.SliceBottom)
            : new ResolvedSprite(metadata.TextureName, metadata.UVMin, metadata.UVMax,
                metadata.SliceLeft, metadata.SliceTop, metadata.SliceRight, metadata.SliceBottom);
    }

    /// <summary>Parses one metadata document. Arrays use `[x, y, width, height]`, `[x, y]`, and `[left, top, right, bottom]`.</summary>
    public static SpriteMetadata? Parse(Stream stream)
    {
        ArgumentNullException.ThrowIfNull(stream);
        try
        {
            using JsonDocument document = JsonDocument.Parse(stream);
            JsonElement root = document.RootElement;
            string texture = RequiredString(root, "texture");
            int[] textureSize = RequiredIntArray(root, "textureSize", 2);
            int[] rect = RequiredIntArray(root, "rect", 4);
            float[] pivot = OptionalFloatArray(root, "pivot", [0.5f, 0.5f], 2);
            float[] border = OptionalFloatArray(root, "border", [0f, 0f, 0f, 0f], 4);
            float pixelsPerUnit = OptionalFloat(root, "pixelsPerUnit", 100f);
            SpriteMetadata metadata = new(texture, textureSize[0], textureSize[1],
                new SpritePixelRect(rect[0], rect[1], rect[2], rect[3]),
                new Vector2(pivot[0], pivot[1]), pixelsPerUnit,
                new Vector4(border[0], border[1], border[2], border[3]));
            return IsValid(metadata) ? metadata : null;
        }
        catch (Exception exception) when (exception is JsonException or InvalidDataException or FormatException
            or OverflowException or KeyNotFoundException or InvalidOperationException or ArgumentException)
        {
            Logger.Warning($"[Sprite] Invalid sprite metadata: {exception.Message}");
            return null;
        }
    }

    /// <summary>Clears cached metadata, used after project asset reload and by tests.</summary>
    public static void ClearCache()
    {
        lock (s_gate) s_cache.Clear();
    }

    private static bool IsValid(SpriteMetadata metadata)
    {
        SpritePixelRect rect = metadata.Rect;
        return !String.IsNullOrWhiteSpace(metadata.TextureName)
            && metadata.TextureWidth > 0 && metadata.TextureHeight > 0
            && rect.X >= 0 && rect.Y >= 0 && rect.Width > 0 && rect.Height > 0
            && rect.X <= metadata.TextureWidth - rect.Width
            && rect.Y <= metadata.TextureHeight - rect.Height
            && float.IsFinite(metadata.PixelsPerUnit) && metadata.PixelsPerUnit > 0f
            && float.IsFinite(metadata.Pivot.X) && float.IsFinite(metadata.Pivot.Y)
            && metadata.Pivot.X is >= 0f and <= 1f && metadata.Pivot.Y is >= 0f and <= 1f
            && float.IsFinite(metadata.Border.X) && float.IsFinite(metadata.Border.Y)
            && float.IsFinite(metadata.Border.Z) && float.IsFinite(metadata.Border.W)
            && metadata.Border.X >= 0f && metadata.Border.Y >= 0f
            && metadata.Border.Z >= 0f && metadata.Border.W >= 0f
            && metadata.Border.X + metadata.Border.Z <= rect.Width
            && metadata.Border.Y + metadata.Border.W <= rect.Height;
    }

    private static string RequiredString(JsonElement root, string name)
    {
        if (!root.TryGetProperty(name, out JsonElement value) || value.ValueKind != JsonValueKind.String
            || String.IsNullOrWhiteSpace(value.GetString()))
        {
            throw new InvalidDataException($"'{name}' must be a non-empty string.");
        }
        return value.GetString()!;
    }

    private static int[] RequiredIntArray(JsonElement root, string name, int count)
    {
        int[] values = root.GetProperty(name).EnumerateArray().Select(value => value.GetInt32()).ToArray();
        if (values.Length != count) throw new InvalidDataException($"'{name}' must have {count} integer values.");
        return values;
    }

    private static float[] OptionalFloatArray(JsonElement root, string name, float[] fallback, int count)
    {
        if (!root.TryGetProperty(name, out JsonElement value)) return fallback;
        float[] values = value.EnumerateArray().Select(item => item.GetSingle()).ToArray();
        if (values.Length != count) throw new InvalidDataException($"'{name}' must have {count} numeric values.");
        return values;
    }

    private static float OptionalFloat(JsonElement root, string name, float fallback) =>
        root.TryGetProperty(name, out JsonElement value) ? value.GetSingle() : fallback;
}
