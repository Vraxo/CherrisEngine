using Cherris.UI;
using Microsoft.JSInterop;

namespace CherrisEditorPhotino.Services;

/// <summary>
/// The Blazor editor's side of theming: loads the catalog, keeps the selection in step with the shared
/// preferences file, and applies the active theme's variables to the document.
/// </summary>
/// <remarks>
/// <para>
/// The ImGui editor uses the same <see cref="ThemeCatalog"/> and the same
/// <see cref="ThemePreferences"/> file, so switching themes in either window is remembered by both; this class
/// only differs in how it applies one — CSS custom properties on the document root instead of ImGui colors.
/// </para>
/// <para>
/// Theme files are the same JSON the ImGui editor reads, from the shipped <c>EditorResources/Themes</c> folder
/// and from the user's theme folder, so a theme dropped into either place shows up in the View menu here as
/// well.
/// </para>
/// </remarks>
public sealed class ThemeService
{
    private ThemeCatalog _catalog = ThemeCatalog.CreateBuiltIn();

    /// <summary>Loaded on first use rather than in the constructor, so construction cannot throw on a bad file.</summary>
    private bool _loaded;

    /// <summary>Raised after the active theme changed, so the menu can redraw.</summary>
    public event Action? Changed;

    /// <summary>The catalog, loaded on first access.</summary>
    public ThemeCatalog Catalog
    {
        get
        {
            EnsureLoaded();
            return _catalog;
        }
    }

    /// <summary>The theme currently applied.</summary>
    public EditorTheme Active => Catalog.Active;

    /// <summary>Files that could not be loaded, for the menu to show.</summary>
    public IReadOnlyList<(string Path, string Error)> Errors => Catalog.Errors;

    /// <summary>Applies the active theme to the document.</summary>
    /// <param name="js">The window's JS runtime.</param>
    public async Task ApplyAsync(IJSRuntime js)
    {
        EnsureLoaded();

        await js.InvokeVoidAsync("cherrisTheme.apply", Catalog.Active.ToCssVariables()).ConfigureAwait(false);
    }

    /// <summary>Switches theme, applies it and remembers the choice.</summary>
    /// <param name="js">The window's JS runtime.</param>
    /// <param name="id">The theme's id.</param>
    public async Task<bool> SelectAsync(IJSRuntime js, string id)
    {
        EnsureLoaded();

        if (!_catalog.Select(id))
        {
            return false;
        }

        ThemePreferences.Save(id);
        await ApplyAsync(js).ConfigureAwait(false);
        Changed?.Invoke();
        return true;
    }

    /// <summary>Rescans the theme folders, keeping the current selection when it still exists.</summary>
    /// <param name="js">The window's JS runtime.</param>
    public async Task ReloadAsync(IJSRuntime js)
    {
        EnsureLoaded();

        string activeId = _catalog.Active.Id;

        foreach (string directory in Directories())
        {
            _catalog.LoadDirectory(directory);
        }

        _catalog.SelectOrDefault(activeId);
        await ApplyAsync(js).ConfigureAwait(false);
        Changed?.Invoke();
    }

    /// <summary>The folders themes are read from: the shipped one first, then the user's.</summary>
    public static IEnumerable<string> Directories()
    {
        return ThemePreferences.DefaultThemeDirectories();
    }

    private void EnsureLoaded()
    {
        if (_loaded)
        {
            return;
        }

        _loaded = true;
        _catalog = ThemeCatalog.Create(Directories());
        _catalog.SelectOrDefault(ThemePreferences.Load());
    }
}
