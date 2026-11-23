using System.Text.Json;

namespace Cherris.UI;

/// <summary>
/// Where themes and the selected theme live on disk.
/// </summary>
/// <remarks>
/// <para>
/// <c>%APPDATA%/CherrisEditor/theme.json</c> holds the selection, so both front ends and every project agree
/// on it. <c>%APPDATA%/CherrisEditor/Themes</c> is where a user drops a JSON theme to add one; it is scanned
/// after the shipped <c>EditorResources/Themes</c> folder.
/// </para>
/// <para>
/// Writes are best effort: a read-only profile must not stop the editor from starting, so a failed save is
/// reported and ignored.
/// </para>
/// </remarks>
public static class ThemePreferences
{
    private static readonly JsonSerializerOptions Options = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true
    };

    /// <summary>The editor's per-user folder.</summary>
    public static string DefaultDirectory => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "CherrisEditor");

    /// <summary>The file holding the selected theme's id.</summary>
    public static string DefaultFilePath => Path.Combine(DefaultDirectory, "theme.json");

    /// <summary>The folder users drop themes into.</summary>
    public static string UserThemeDirectory => Path.Combine(DefaultDirectory, "Themes");

    /// <summary>The themes that ship with the editor.</summary>
    public static string ShippedThemeDirectory => Path.Combine(
        AppContext.BaseDirectory, "EditorResources", "Themes");

    /// <summary>
    /// The folders themes are read from, in order: the shipped folder first, so a file with a built-in theme's
    /// id overrides it, then the user's folder, which wins over both.
    /// </summary>
    public static IEnumerable<string> DefaultThemeDirectories()
    {
        yield return ShippedThemeDirectory;
        yield return UserThemeDirectory;
    }

    /// <summary>Reads the selected theme's id, or null when nothing has been saved yet.</summary>
    /// <param name="filePath">Settings file; defaults to <see cref="DefaultFilePath"/>.</param>
    public static string? Load(string? filePath = null)
    {
        string path = filePath ?? DefaultFilePath;

        try
        {
            if (!File.Exists(path))
            {
                return null;
            }

            SelectionDocument? document = JsonSerializer.Deserialize<SelectionDocument>(File.ReadAllText(path), Options);
            return String.IsNullOrWhiteSpace(document?.Theme) ? null : document.Theme;
        }
        catch (Exception exception) when (exception is IOException or JsonException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    /// <summary>Saves the selected theme's id.</summary>
    /// <param name="themeId">The theme's id.</param>
    /// <param name="filePath">Settings file; defaults to <see cref="DefaultFilePath"/>.</param>
    /// <returns>False when the file could not be written.</returns>
    public static bool Save(string themeId, string? filePath = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(themeId);

        string path = filePath ?? DefaultFilePath;

        try
        {
            string? directory = Path.GetDirectoryName(path);

            if (!String.IsNullOrEmpty(directory))
            {
                Directory.CreateDirectory(directory);
            }

            File.WriteAllText(path, JsonSerializer.Serialize(new SelectionDocument { Theme = themeId }, Options));
            return true;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return false;
        }
    }

    private sealed class SelectionDocument
    {
        public string? Theme { get; set; }
    }
}
