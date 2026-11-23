using Cherris.UI;
using ImGuiNET;

namespace CherrisEditor;

/// <summary>
/// Owns the editor's theme selection for the ImGui front end: loads the catalog, remembers the choice, and
/// reapplies the ImGui style and font when it changes.
/// </summary>
/// <remarks>
/// <para>
/// Themes are loaded from the shipped <c>EditorResources/Themes</c> folder and from the user's theme folder,
/// so a JSON file dropped in either place appears in the View menu without a rebuild. The selection is stored
/// in <see cref="ThemePreferences"/> so the next launch — and the other front end — starts with the same theme.
/// </para>
/// <para>
/// The font atlas is built once, for every theme (see <c>ImGuiFontLoader</c>); switching themes only moves
/// <c>io.FontDefault</c> and repaints the style.
/// </para>
/// </remarks>
public static class EditorThemes
{
    private static IReadOnlyDictionary<string, ImFontPtr> _fonts = new Dictionary<string, ImFontPtr>();
    private static bool _initialized;

    /// <summary>The catalog, built on first use.</summary>
    public static ThemeCatalog Catalog { get; private set; } = ThemeCatalog.CreateBuiltIn();

    /// <summary>Raised after a theme has been applied.</summary>
    public static event Action<EditorTheme>? Changed;

    /// <summary>Loads the catalog and selects the saved theme.</summary>
    public static void Initialize()
    {
        if (_initialized)
        {
            return;
        }

        _initialized = true;

        Catalog = ThemeCatalog.Create(ThemeDirectories());
        Catalog.SelectOrDefault(ThemePreferences.Load());
    }

    /// <summary>The folders themes are loaded from, shipped first so a user theme wins.</summary>
    public static IEnumerable<string> ThemeDirectories()
    {
        return ThemePreferences.DefaultThemeDirectories();
    }

    /// <summary>Gives the service the atlas entries built by the font loader.</summary>
    /// <param name="fonts">Font per theme id.</param>
    public static void AttachFonts(IReadOnlyDictionary<string, ImFontPtr> fonts)
    {
        _fonts = fonts;
    }

    /// <summary>Applies the active theme to ImGui and switches to its font.</summary>
    public static void Reapply()
    {
        Initialize();

        EditorTheme theme = Catalog.Active;

        ImGuiTheme.Apply(theme);

        if (_fonts.TryGetValue(theme.Id, out ImFontPtr font))
        {
            ImGui.GetIO().FontDefault = font;
        }

        Changed?.Invoke(theme);
    }

    /// <summary>Switches to a theme, applies it and remembers the choice.</summary>
    /// <param name="id">The theme's id.</param>
    /// <returns>False when no theme has that id.</returns>
    public static bool Select(string id)
    {
        Initialize();

        if (!Catalog.Select(id))
        {
            return false;
        }

        ThemePreferences.Save(id);
        Reapply();
        return true;
    }

    /// <summary>Reloads theme files from disk, keeping the current selection when it still exists.</summary>
    public static void ReloadFiles()
    {
        Initialize();

        string activeId = Catalog.Active.Id;

        foreach (string directory in ThemeDirectories())
        {
            Catalog.LoadDirectory(directory);
        }

        Catalog.SelectOrDefault(activeId);
        Reapply();
    }
}
