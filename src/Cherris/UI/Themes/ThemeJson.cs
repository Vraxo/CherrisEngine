using System.Text.Json;
using System.Text.Json.Serialization;

namespace Cherris.UI;

/// <summary>
/// Reads and writes themes as JSON, which is how a theme is stored on disk and how new themes are added
/// without recompiling either editor.
/// </summary>
/// <remarks>
/// <para>The shape is the theme itself, with colors as hex strings:</para>
/// <code>
/// {
///   "id": "unity-dark",
///   "displayName": "Unity (dark)",
///   "provenance": "Dark column of Unity's --unity-colors-* palette.",
///   "typography": {
///     "family": "Inter", "monospaceFamily": "Roboto Mono",
///     "bodyPixels": 12, "smallPixels": 10, "largePixels": 14,
///     "fontResource": "Fonts/Inter-Regular.TTF",
///     "fallbackFontResource": "Fonts/RobotoMono-Regular.TTF",
///     "cssStack": "\"Inter\", Verdana, system-ui, sans-serif"
///   },
///   "metrics": { "cornerRadius": 3, "borderWidth": 1, "controlHeight": 18, "scrollbarWidth": 12 },
///   "colors": { "panel": "#383838", "foreground": "#d2d2d2", "...": "one entry per role" }
/// }
/// </code>
/// <para>
/// Reading is strict on purpose: a theme that is missing a role, names an unknown one or spells a color
/// badly is rejected with a reason, rather than silently leaving a widget unthemed.
/// </para>
/// </remarks>
public static class ThemeJson
{
    private static readonly JsonSerializerOptions Options = new()
    {
        PropertyNameCaseInsensitive = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    /// <summary>Serializes a theme to JSON.</summary>
    /// <param name="theme">The theme to write.</param>
    public static string Write(EditorTheme theme)
    {
        ArgumentNullException.ThrowIfNull(theme);

        ThemeDocument document = new()
        {
            Id = theme.Id,
            DisplayName = theme.DisplayName,
            Provenance = theme.Provenance,
            Typography = new TypographyDocument
            {
                Family = theme.Typography.Family,
                MonospaceFamily = theme.Typography.MonospaceFamily,
                BodyPixels = theme.Typography.BodyPixels,
                SmallPixels = theme.Typography.SmallPixels,
                LargePixels = theme.Typography.LargePixels,
                FontResource = theme.Typography.FontResource,
                FallbackFontResource = theme.Typography.FallbackFontResource,
                CssStack = theme.Typography.CssStack,
                BoldWeight = theme.Typography.BoldWeight
            },
            Metrics = new MetricsDocument
            {
                CornerRadius = theme.Metrics.CornerRadius,
                BorderWidth = theme.Metrics.BorderWidth,
                ControlHeight = theme.Metrics.ControlHeight,
                ScrollbarWidth = theme.Metrics.ScrollbarWidth
            },
            Colors = new Dictionary<string, string>(StringComparer.Ordinal)
        };

        foreach (string role in ThemeRoles.All)
        {
            document.Colors[role] = theme[role].Hex;
        }

        return JsonSerializer.Serialize(document, Options);
    }

    /// <summary>Reads a theme from JSON text.</summary>
    /// <param name="json">The document.</param>
    /// <param name="error">Why the document was rejected, or null.</param>
    /// <param name="isExternal">Whether the theme came from disk rather than from code.</param>
    public static EditorTheme? Read(string json, out string? error, bool isExternal = true)
    {
        ArgumentNullException.ThrowIfNull(json);

        ThemeDocument? document;

        try
        {
            document = JsonSerializer.Deserialize<ThemeDocument>(json, Options);
        }
        catch (JsonException exception)
        {
            error = "The file is not valid JSON: " + exception.Message;
            return null;
        }

        if (document is null)
        {
            error = "The file is empty.";
            return null;
        }

        if (String.IsNullOrWhiteSpace(document.Id))
        {
            error = "'id' is required.";
            return null;
        }

        if (String.IsNullOrWhiteSpace(document.DisplayName))
        {
            error = "'displayName' is required.";
            return null;
        }

        if (document.Typography is null || document.Metrics is null || document.Colors is null)
        {
            error = "'typography', 'metrics' and 'colors' are all required.";
            return null;
        }

        Dictionary<string, ThemeColor> colors = new(StringComparer.Ordinal);

        foreach (string role in ThemeRoles.All)
        {
            if (!document.Colors.TryGetValue(role, out string? hex) || String.IsNullOrWhiteSpace(hex))
            {
                error = $"'colors.{role}' is missing.";
                return null;
            }

            try
            {
                colors[role] = ThemeColor.FromHex(hex);
            }
            catch (Exception exception) when (exception is FormatException or ArgumentException or OverflowException)
            {
                error = $"'colors.{role}' is not a hex color: {hex}";
                return null;
            }
        }

        string[] unknown = document.Colors.Keys
            .Where(role => !ThemeRoles.All.Contains(role, StringComparer.Ordinal))
            .Order(StringComparer.Ordinal)
            .ToArray();

        if (unknown.Length > 0)
        {
            error = "'colors' names roles that do not exist: " + String.Join(", ", unknown);
            return null;
        }

        ThemeTypography typography = new(
            document.Typography.Family ?? "Inter",
            document.Typography.MonospaceFamily ?? document.Typography.Family ?? "Inter",
            document.Typography.BodyPixels,
            document.Typography.SmallPixels,
            document.Typography.LargePixels,
            document.Typography.FontResource ?? string.Empty,
            document.Typography.FallbackFontResource ?? string.Empty,
            document.Typography.CssStack ?? $"\"{document.Typography.Family ?? "Inter"}\", system-ui, sans-serif",
            document.Typography.BoldWeight);

        error = null;

        return new EditorTheme
        {
            Id = document.Id,
            DisplayName = document.DisplayName,
            Provenance = document.Provenance ?? string.Empty,
            Typography = typography,
            Metrics = new ThemeMetrics(
                document.Metrics.CornerRadius,
                document.Metrics.BorderWidth,
                document.Metrics.ControlHeight,
                document.Metrics.ScrollbarWidth),
            Colors = colors,
            IsExternal = isExternal
        };
    }

    /// <summary>Reads a theme from a file.</summary>
    /// <param name="path">The JSON file.</param>
    /// <param name="error">Why the file was rejected, or null.</param>
    public static EditorTheme? ReadFile(string path, out string? error)
    {
        try
        {
            return Read(File.ReadAllText(path), out error);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            error = exception.Message;
            return null;
        }
    }

    private sealed class ThemeDocument
    {
        public string? Id { get; set; }

        public string? DisplayName { get; set; }

        public string? Provenance { get; set; }

        public TypographyDocument? Typography { get; set; }

        public MetricsDocument? Metrics { get; set; }

        public Dictionary<string, string>? Colors { get; set; }
    }

    private sealed class TypographyDocument
    {
        public string? Family { get; set; }

        public string? MonospaceFamily { get; set; }

        public float BodyPixels { get; set; }

        public float SmallPixels { get; set; }

        public float LargePixels { get; set; }

        public string? FontResource { get; set; }

        public string? FallbackFontResource { get; set; }

        public string? CssStack { get; set; }

        public int BoldWeight { get; set; } = 700;
    }

    private sealed class MetricsDocument
    {
        public float CornerRadius { get; set; }

        public float BorderWidth { get; set; } = 1f;

        public float ControlHeight { get; set; }

        public float ScrollbarWidth { get; set; }
    }
}
