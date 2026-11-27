using Overcore.Utils;

namespace Overcore.Rendering;

/// <summary>Resolves project-owned TrueType font files and the engine's bundled fallback font.</summary>
public static class ProjectFontAssets
{
    private const string BuiltInFontFileName = "RobotoMono-Regular.TTF";
    private static readonly string[] s_arabicFallbackFontFiles = ["NotoSansArabic.ttf", "DejaVuSans.ttf"];

    /// <summary>Finds a named font file from the active project or returns the bundled fallback for a blank name.</summary>
    public static string? Find(string? fontName)
    {
        if (!String.IsNullOrWhiteSpace(fontName))
        {
            try
            {
                return ProjectFiles.Find(fontName);
            }
            catch (Exception exception) when (exception is ArgumentException or NotSupportedException or PathTooLongException)
            {
                return null;
            }
        }

        string bundledPath = Path.Combine(AppContext.BaseDirectory, "Resources", "Fonts", BuiltInFontFileName);
        if (File.Exists(bundledPath))
        {
            return bundledPath;
        }

        return EditorResources.Find(Path.Combine("Fonts", BuiltInFontFileName));
    }

    /// <summary>Finds the bundled Arabic/Persian fallback font shipped with the engine.</summary>
    public static string? FindArabicFallback()
    {
        string? defaultFont = Find(null);
        string? fontDirectory = defaultFont is null ? null : Path.GetDirectoryName(defaultFont);
        foreach (string fontFileName in s_arabicFallbackFontFiles)
        {
            string bundledPath = Path.Combine(AppContext.BaseDirectory, "Resources", "Fonts", fontFileName);
            if (File.Exists(bundledPath)) return bundledPath;
            if (fontDirectory is null) continue;
            string siblingPath = Path.Combine(fontDirectory, fontFileName);
            if (File.Exists(siblingPath)) return siblingPath;
        }
        return null;
    }
}
