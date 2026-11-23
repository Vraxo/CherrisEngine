namespace Cherris.UI;

/// <summary>
/// The themes the editors can wear: the two built-in ones, plus every JSON theme found in the shipped
/// <c>Themes</c> folder and in the user's theme folder.
/// </summary>
/// <remarks>
/// <para>
/// Loading is deliberately boring: a JSON theme with the same <c>id</c> as a built-in replaces it, so a
/// shipped theme can be tuned without a rebuild, and a theme with a new <c>id</c> simply appears in the
/// menu. Files that fail to parse are collected in <see cref="Errors"/> instead of being thrown, so one
/// broken theme cannot stop the editor from starting.
/// </para>
/// <para>
/// One <see cref="ThemeCatalog"/> is the active selection as well: both front ends call
/// <see cref="Select"/> and read <see cref="Active"/>, and the ImGui editor's font atlas and the Blazor
/// editor's CSS variables are re-derived from it.
/// </para>
/// </remarks>
public sealed class ThemeCatalog
{
    private readonly List<EditorTheme> _themes = [];

    /// <summary>The built-in themes, in menu order.</summary>
    public static IReadOnlyList<EditorTheme> BuiltIn { get; } =
    [
        Ue5StarshipTheme.Create(),
        UnityDarkTheme.Create()
    ];

    /// <summary>The theme used when the settings name one that does not exist.</summary>
    public static EditorTheme Default => BuiltIn[0];

    /// <summary>Every theme, built-in ones first, in the order they were loaded.</summary>
    public IReadOnlyList<EditorTheme> Themes => _themes;

    /// <summary>Files that could not be loaded, with the reason.</summary>
    public IReadOnlyList<(string Path, string Error)> Errors => _errors;

    private readonly List<(string Path, string Error)> _errors = [];

    /// <summary>The theme currently applied.</summary>
    public EditorTheme Active { get; private set; } = Default;

    /// <summary>Raised after <see cref="Active"/> changes.</summary>
    public event Action<EditorTheme>? ActiveChanged;

    /// <summary>A catalog with the built-in themes only.</summary>
    public static ThemeCatalog CreateBuiltIn()
    {
        return new ThemeCatalog();
    }

    /// <summary>
    /// A catalog with the built-in themes plus every <c>*.json</c> theme in <paramref name="directories"/>,
    /// later directories winning over earlier ones and over the built-ins.
    /// </summary>
    /// <param name="directories">Folders to scan; missing folders are skipped.</param>
    public static ThemeCatalog Create(IEnumerable<string> directories)
    {
        ThemeCatalog catalog = new();

        foreach (string directory in directories)
        {
            catalog.LoadDirectory(directory);
        }

        return catalog;
    }

    private ThemeCatalog()
    {
        _themes.AddRange(BuiltIn);
    }

    /// <summary>Adds or replaces every theme in a folder.</summary>
    /// <param name="directory">Folder to scan for <c>*.json</c>.</param>
    public void LoadDirectory(string directory)
    {
        if (String.IsNullOrWhiteSpace(directory) || !Directory.Exists(directory))
        {
            return;
        }

        IEnumerable<string> files;

        try
        {
            files = Directory.EnumerateFiles(directory, "*.json", SearchOption.TopDirectoryOnly)
                .Order(StringComparer.OrdinalIgnoreCase)
                .ToArray();
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            _errors.Add((directory, exception.Message));
            return;
        }

        foreach (string file in files)
        {
            EditorTheme? theme = ThemeJson.ReadFile(file, out string? error);

            if (theme is null)
            {
                _errors.Add((file, error ?? "Unknown error."));
                continue;
            }

            Add(theme);
        }
    }

    /// <summary>Adds a theme, replacing one with the same id and keeping the active one active.</summary>
    /// <param name="theme">The theme to add.</param>
    public void Add(EditorTheme theme)
    {
        ArgumentNullException.ThrowIfNull(theme);

        bool wasActive = String.Equals(Active.Id, theme.Id, StringComparison.Ordinal);
        int index = _themes.FindIndex(existing => String.Equals(existing.Id, theme.Id, StringComparison.Ordinal));

        if (index >= 0)
        {
            _themes[index] = theme;
        }
        else
        {
            _themes.Add(theme);
        }

        // A theme file that replaces the active theme takes effect immediately.
        if (wasActive)
        {
            SetActive(theme, raiseEvent: true);
        }
    }

    /// <summary>Makes a theme active.</summary>
    /// <param name="id">The theme's id.</param>
    /// <returns>False when no theme has that id; the active theme is then unchanged.</returns>
    public bool Select(string id)
    {
        if (Find(id) is not { } theme)
        {
            return false;
        }

        SetActive(theme, raiseEvent: true);
        return true;
    }

    /// <summary>Finds a theme by id.</summary>
    /// <param name="id">The theme's id.</param>
    public EditorTheme? Find(string id)
    {
        return _themes.FirstOrDefault(theme => String.Equals(theme.Id, id, StringComparison.Ordinal));
    }

    /// <summary>Selects <paramref name="id"/>, falling back to the default theme when it does not exist.</summary>
    /// <param name="id">The theme's id, usually from the settings file.</param>
    public EditorTheme SelectOrDefault(string? id)
    {
        if (!String.IsNullOrWhiteSpace(id) && Find(id) is { } theme)
        {
            SetActive(theme, raiseEvent: false);
            return theme;
        }

        SetActive(Default, raiseEvent: false);
        return Active;
    }

    private void SetActive(EditorTheme theme, bool raiseEvent)
    {
        bool changed = !ReferenceEquals(theme, Active);

        Active = theme;

        if (raiseEvent && changed)
        {
            ActiveChanged?.Invoke(theme);
        }
    }
}
