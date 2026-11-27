using Overcore.Utils;

namespace Overcore.Rendering;

/// <summary>Resolves project-owned TrueType font files and the engine's bundled fallback font.</summary>
public static class ProjectFontAssets
{
    private const string BuiltInFontFileName = "RobotoMono-Regular.TTF";

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
}
