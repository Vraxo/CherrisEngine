using System.Globalization;
using System.Numerics;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Routing;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.Extensions.Logging;
using Microsoft.JSInterop;
using Overcore.Projects;
using OvercoreEditorPhotino;
using OvercoreEditorPhotino.Components;
using OvercoreEditorPhotino.Shared;
using OvercoreEditorPhotino.Services;
using OvercoreEditorSession;
using Overcore.Scripting;

namespace OvercoreEditorPhotino.Components;

/// <summary>
/// Code-behind for <see cref="ContentBrowserPanel"/>: folder navigation, previews and the context menu.
/// The markup lives in <c>ContentBrowserPanel.razor</c>; this file holds the state and handlers.
/// </summary>
public partial class ContentBrowserPanel
{
    private static readonly TimeSpan PollInterval = TimeSpan.FromSeconds(3);

    private readonly List<Entry> _entries = [];
    private string? _currentDirectory;
    private string? _selectedPath;
    private Timer? _timer;

    // The context menu: open or not, where it was opened, the tile it was opened on, and the folder anything it
    // creates goes into.
    private bool _menuOpen;
    private double _menuX;
    private double _menuY;
    private Entry? _menuTarget;
    private string? _menuDirectory;

    // The creation dialog, and what it is creating. The folder is fixed when the menu closes: it is the folder the
    // user right-clicked, not wherever the browser has moved to by the time they finish typing.
    private CreateKind _dialogKind;
    private string? _dialogDirectory;
    private string _dialogName = string.Empty;

    // A script is Lua or C#; the dialog asks which, and starts on Lua so the flow it always had stays one keystroke.
    private ScriptKind _dialogScriptKind = ScriptKind.Lua;

    private sealed record Entry(string FullPath, string Name, bool IsDirectory);

    /// <summary>What the context menu can create.</summary>
    private enum CreateKind
    {
        /// <summary>No dialog is open.</summary>
        None,

        /// <summary>A scene file: a camera and a sun, opened when it is written.</summary>
        Scene,

        /// <summary>A Lua script, which always lands in <c>Scripts/</c>.</summary>
        Script,

        /// <summary>An empty folder, somewhere to put the other two.</summary>
        Folder
    }

    private string ProjectRoot => Host.ProjectRoot ?? string.Empty;

    private bool IsAtRoot => _currentDirectory is null
        || string.Equals(_currentDirectory, ProjectRoot, StringComparison.OrdinalIgnoreCase);

    private string DisplayPath
    {
        get
        {
            if (Host.ProjectRoot is not string root)
            {
                return "none";
            }

            string projectName = Host.SessionName ?? Path.GetFileName(root.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));

            if (string.IsNullOrEmpty(projectName))
            {
                projectName = "Project";
            }

            if (_currentDirectory is null || string.Equals(_currentDirectory, root, StringComparison.OrdinalIgnoreCase))
            {
                return projectName;
            }

            string relative = Path.GetRelativePath(root, _currentDirectory);

            return $"{projectName}/{relative.Replace(Path.DirectorySeparatorChar, '/')}";
        }
    }

    protected override void OnInitialized()
    {
        _currentDirectory = Host.ProjectRoot;
        Refresh();
        Previews.Changed += OnPreviewsChanged;
        _timer = new Timer(_ => _ = PollAsync(), null, PollInterval, PollInterval);
    }

    private void OnPreviewsChanged()
    {
        // Previews are read on a worker thread; the renderer is the only one allowed to redraw the panel.
        _ = InvokeAsync(StateHasChanged);
    }

    private async Task PollAsync()
    {
        // The asset folder changes outside the editor, so re-read it; ImGui re-reads every frame.
        string? previous = string.Join('\n', _entries.Select(entry => entry.FullPath));
        List<Entry> entries = ReadEntries();
        string current = string.Join('\n', entries.Select(entry => entry.FullPath));

        if (previous == current)
        {
            return;
        }

        await InvokeAsync(() =>
        {
            _entries.Clear();
            _entries.AddRange(entries);

            // A menu left open over a folder that changed underneath it names files that may no longer be there.
            _menuOpen = false;

            StateHasChanged();
        });

        LoadPreviews();
    }

    private void Refresh()
    {
        _entries.Clear();
        _entries.AddRange(ReadEntries());
        LoadPreviews();
    }

    /// <summary>Asks for the previews of the scenes in this folder; unchanged files are not read again.</summary>
    private void LoadPreviews()
    {
        _ = Previews.LoadAsync(_entries.Where(IsScene).Select(entry => entry.FullPath));
    }

    /// <summary>
    /// A candidate for a preview. <c>project.yaml</c> is the project descriptor rather than a scene, and a file
    /// that turns out not to be a scene simply keeps its icon.
    /// </summary>
    private static bool IsScene(Entry entry)
    {
        string extension = Path.GetExtension(entry.Name);

        return !entry.IsDirectory
            && (extension.Equals(".yaml", StringComparison.OrdinalIgnoreCase)
                || extension.Equals(".yml", StringComparison.OrdinalIgnoreCase))
            && !entry.Name.Equals(ProjectConfig.FileName, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>A prefab asset: the tile and the menu offer adding one to the scene.</summary>
    private static bool IsPrefab(Entry entry) =>
        !entry.IsDirectory
        && Path.GetExtension(entry.Name).Equals(".prefab", StringComparison.OrdinalIgnoreCase);

    /// <summary>The name, plus what the preview found in the file.</summary>
    private string EntryTitle(Entry entry)
    {
        if (Previews.Get(entry.FullPath) is not SceneThumbnail preview)
        {
            // A prefab says what it is and what the tile does with it, since the icon alone does not.
            return IsPrefab(entry)
                ? $"{entry.Name} — a prefab; double-click or right-click to add one to the scene"
                : entry.Name;
        }

        string contents = preview.Objects.Count switch
        {
            0 => "empty scene",
            1 => "1 object",
            int count => $"{count} objects"
        };

        // A scene is also something to open, so its tile says so rather than only describing the file.
        return IsScene(entry)
            ? $"{entry.Name} — {contents}; double-click to open"
            : $"{entry.Name} — {contents}";
    }

    private List<Entry> ReadEntries()
    {
        List<Entry> entries = [];
        string? directory = _currentDirectory ?? Host.ProjectRoot;

        if (directory is null || !Directory.Exists(directory))
        {
            return entries;
        }

        try
        {
            foreach (string path in Directory.EnumerateDirectories(directory).OrderBy(path => path, StringComparer.OrdinalIgnoreCase))
            {
                entries.Add(new Entry(path, Path.GetFileName(path), true));
            }

            foreach (string path in Directory.EnumerateFiles(directory).OrderBy(path => path, StringComparer.OrdinalIgnoreCase))
            {
                string name = Path.GetFileName(path);

                if (name.EndsWith(".meta", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                entries.Add(new Entry(path, name, false));
            }
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            // A locked or permission-denied folder shows as empty instead of failing the panel.
        }

        return entries;
    }

    private void Select(Entry entry)
    {
        _selectedPath = entry.FullPath;
    }

    /// <summary>
    /// Double-click: a folder opens in the browser, and a scene opens in the editor.
    /// </summary>
    /// <remarks>
    /// The panel opens what it shows as a scene and leaves everything else inert. It does not wait to have read the
    /// file first: the session reads it either way, and a YAML that turns out not to be a scene leaves the open scene
    /// alone and says so in the hierarchy, which is a better answer than a double-click that does nothing.
    /// </remarks>
    private void Activate(Entry entry)
    {
        if (entry.IsDirectory)
        {
            _currentDirectory = entry.FullPath;
            _selectedPath = null;
            Refresh();
            return;
        }

        if (IsScene(entry))
        {
            Host.OpenScene(entry.FullPath);
        }
        else if (IsPrefab(entry))
        {
            // Unity opens a prefab for editing; Overcore has no prefab mode, so double-click does the one
            // honest thing it can: add one to the scene.
            Host.InstantiatePrefab(entry.FullPath);
        }
    }

    private void GoBack()
    {
        if (_currentDirectory is null)
        {
            return;
        }

        string? parent;

        try
        {
            parent = Directory.GetParent(_currentDirectory)?.FullName;
        }
        catch (Exception exception) when (exception is ArgumentException or IOException)
        {
            parent = null;
        }

        // Never walk above the project: the panel shows one project's assets, like ImGui's browser.
        _currentDirectory = parent is null || !parent.StartsWith(ProjectRoot, StringComparison.OrdinalIgnoreCase)
            ? Host.ProjectRoot
            : parent;

        _selectedPath = null;
        Refresh();
    }

    private void InstantiateFromMenu(Entry entry)
    {
        CloseMenu();
        Host.InstantiatePrefab(entry.FullPath);
    }

    private static RenderFragment Glyph(Entry entry)
    {
        string extension = Path.GetExtension(entry.Name).ToLowerInvariant();

        string path = entry.IsDirectory
            ? "M1.5 3.2h4.2l1.3 1.7h7.5v8.4H1.5z"
            : extension switch
            {
                ".png" or ".jpg" or ".jpeg" or ".bmp" or ".gif" or ".webp" or ".tga" =>
                    "M2 3.2h12v9.6H2zm1.2 1.2v5.2l2.6-2.6 2.2 2.2 2.4-2.9 2.4 2.6V4.4z",
                ".yaml" or ".yml" => "M3 2.4h7l3 3v8.2H3zm1.2 3.4h6.6M4.2 8h6.6M4.2 9.8h4.4",
                ".prefab" => "M8 1.8 13.4 4.6v6.8L8 14.2 2.6 11.4V4.6zm0 2.8v6.8m-5.4-9.6L8 7.4l5.4-2.8",
                ".wav" or ".ogg" or ".mp3" => "M6 2.6v8.1a1.7 1.7 0 1 0 1.4 1.7V6.2l5-1.1v5.6a1.7 1.7 0 1 0 1.4 1.7V2.2z",
                _ => "M3.2 2h6.2l3.4 3.4V14H3.2zm1.2 3.2h6.4M4.4 7.4h6.4M4.4 9.6h4.6"
            };

        return builder =>
        {
            builder.OpenElement(0, "svg");
            builder.AddAttribute(1, "class", entry.IsDirectory ? "cb-glyph folder" : "cb-glyph");
            builder.AddAttribute(2, "viewBox", "0 0 16 16");
            builder.AddAttribute(3, "aria-hidden", "true");

            builder.OpenElement(4, "path");
            builder.AddAttribute(5, "d", path);
            builder.AddAttribute(6, "fill", entry.IsDirectory || extension is ".png" or ".jpg" or ".jpeg" or ".bmp" or ".gif" or ".webp" or ".tga" ? "currentColor" : "none");
            builder.AddAttribute(7, "stroke", "currentColor");
            builder.AddAttribute(8, "stroke-width", "1.1");
            builder.CloseElement();

            builder.CloseElement();
        };
    }

    // ---- context menu ----

    private string MenuStyle =>
        $"left: min({_menuX.ToString(CultureInfo.InvariantCulture)}px, calc(100vw - 232px)); " +
        $"top: min({_menuY.ToString(CultureInfo.InvariantCulture)}px, calc(100vh - 200px))";

    /// <summary>
    /// Opens the menu where the pointer is. A tile decides the folder: a folder is created in itself, everything else
    /// in the folder being shown, which is what right-clicking the tile of a scene means.
    /// </summary>
    private void OpenMenuAtPointer(MouseEventArgs args) => OpenMenu(args, null);

    private void OpenMenu(MouseEventArgs args, Entry? entry)
    {
        _menuX = args.ClientX;
        _menuY = args.ClientY;
        _menuTarget = entry;

        if (entry is { IsDirectory: true } folder)
        {
            _menuDirectory = folder.FullPath;
        }
        else
        {
            _menuDirectory = _currentDirectory ?? Host.ProjectRoot;
        }

        // With no project there is no folder to write into; the panel already says so.
        _menuOpen = _menuDirectory is not null;
    }

    private void CloseMenu()
    {
        _menuOpen = false;
        _menuTarget = null;
    }

    private void RefreshFromMenu()
    {
        CloseMenu();
        Refresh();
    }

    private void ActivateFromMenu(Entry entry)
    {
        CloseMenu();
        Activate(entry);
    }

    /// <summary>
    /// Scripts live in <c>Scripts/</c> and nowhere else — the engine runs a Lua script from there and compiles a C#
    /// one into a component — so the menu offers one from the project root and from that folder, and says why
    /// elsewhere.
    /// </summary>
    private bool CanCreateScript =>
        _menuDirectory is string directory &&
        Host.ProjectRoot is string root &&
        (IsSameFolder(directory, root) || IsSameFolder(directory, Path.Combine(root, ScriptTemplate.Folder)));

    private string ScriptMenuTitle => CanCreateScript
        ? $"Write a script into {ScriptTemplate.Folder}/, in Lua or C#"
        : $"Scripts live in {ScriptTemplate.Folder}/: open that folder to add one";

    // ---- creating ----

    private void BeginCreate(CreateKind kind)
    {
        _dialogDirectory = _menuDirectory;
        _dialogKind = kind;

        string? root = Host.ProjectRoot;

        switch (kind)
        {
            case CreateKind.Scene:
                _dialogName = _dialogDirectory is string sceneFolder
                    ? ProjectFileName.Available(sceneFolder, SceneTemplate.DefaultName, SceneTemplate.Extension)
                    : SceneTemplate.DefaultName;
                break;

            case CreateKind.Script:
                // Fixed by the engine's convention rather than by where the menu was opened. The language is the
                // dialog's to change; the folder is not.
                _dialogDirectory = root is null ? null : Path.Combine(root, ScriptTemplate.Folder);
                _dialogScriptKind = ScriptKind.Lua;
                _dialogName = ProjectFileName.Available(
                    _dialogDirectory ?? string.Empty, "NewScript", ScriptTemplate.ExtensionOf(_dialogScriptKind));
                break;

            default:
                _dialogName = _dialogDirectory is string folderFolder
                    ? ProjectFileName.Available(folderFolder, "New Folder", string.Empty)
                    : "New Folder";
                break;
        }

        CloseMenu();
    }

    private void CloseDialog() => _dialogKind = CreateKind.None;

    /// <summary>The language row, shown only while a script dialog is open.</summary>
    private IReadOnlyList<string>? LanguageChoices => _dialogKind == CreateKind.Script
        ? ScriptLanguages
        : null;

    private static readonly string[] ScriptLanguages =
        [ScriptTemplate.DisplayName(ScriptKind.Lua), ScriptTemplate.DisplayName(ScriptKind.CSharp)];

    private string LanguageChoice => ScriptTemplate.DisplayName(_dialogScriptKind);

    /// <summary>
    /// Switches what Create will write. The typed name is kept: both languages share the naming rule, and switching
    /// after typing should not cost the typing.
    /// </summary>
    private void ChooseLanguage(string choice) =>
        _dialogScriptKind = String.Equals(choice, ScriptTemplate.DisplayName(ScriptKind.CSharp), StringComparison.Ordinal)
            ? ScriptKind.CSharp
            : ScriptKind.Lua;

    private string DialogTitle => _dialogKind switch
    {
        CreateKind.Scene => "New Scene",
        CreateKind.Script => "New Script",
        _ => "New Folder"
    };

    private string DialogText => _dialogKind switch
    {
        CreateKind.Scene =>
            "A new scene starts with a camera and a sun, so it opens with something to look through and something to " +
            "light it. It is written into this folder and opened.",
        CreateKind.Script => _dialogScriptKind == ScriptKind.CSharp
            ? $"A C# script is written into {ScriptTemplate.Folder}/ and compiled into a component straight away, so " +
                "the Add Component menu offers it as soon as this closes."
            : $"A Lua script is written into {ScriptTemplate.Folder}/, where the engine looks for scripts, and the " +
                "browser follows it there.",
        _ => "An empty folder, somewhere to put scenes and scripts. Nothing is written inside it."
    };

    private string DialogLabel => _dialogKind switch
    {
        CreateKind.Scene => "Scene name",
        CreateKind.Script => "Script name",
        _ => "Folder name"
    };

    private string DialogHint(string typed)
    {
        string root = Host.ProjectRoot ?? string.Empty;

        if (_dialogKind == CreateKind.Script)
        {
            return ScriptTemplate.FullPath(root, typed, _dialogScriptKind) is { } path
                ? $"Saves to {ProjectFileName.Describe(path, root)}"
                : "A script name starts with a letter or underscore, then letters, digits or underscores.";
        }

        string extension = _dialogKind == CreateKind.Scene ? SceneTemplate.Extension : string.Empty;
        string verb = _dialogKind == CreateKind.Folder ? "Creates" : "Saves to";

        return ProjectFileName.TryPreview(_dialogDirectory, typed, extension, out string preview)
            ? $"{verb} {ProjectFileName.Describe(preview, root)}"
            : "Type a name to see what it becomes.";
    }

    /// <summary>Creates what the dialog asked for, and shows it; an error leaves the dialog open with the reason.</summary>
    private string? CreateItem(string name)
    {
        string? error = null;
        string? created;

        if (_dialogKind == CreateKind.Scene)
        {
            created = Host.CreateScene(name, _dialogDirectory, out error);
        }
        else if (_dialogKind == CreateKind.Script)
        {
            created = Host.CreateScript(name, _dialogScriptKind, out error);
        }
        else if (_dialogKind == CreateKind.Folder)
        {
            created = Host.CreateFolder(name, _dialogDirectory, out error);
        }
        else
        {
            return "Nothing to create.";
        }

        if (created is null)
        {
            return error ?? "Nothing was created.";
        }

        ShowCreated(created);

        return null;
    }

    /// <summary>Points the browser at what was just created, so it is seen rather than merely present.</summary>
    private void ShowCreated(string path)
    {
        // A script lands in Scripts/ wherever the menu was opened, so following the file is the only way it is seen.
        // A created folder is selected in the folder that holds it, which is where the menu was opened.
        if (Path.GetDirectoryName(path) is { } folder && !IsSameFolder(folder, _currentDirectory))
        {
            _currentDirectory = folder;
        }

        Refresh();
        _selectedPath = path;
    }

    private static bool IsSameFolder(string? left, string? right) =>
        left is not null &&
        right is not null &&
        string.Equals(
            Path.TrimEndingDirectorySeparator(left),
            Path.TrimEndingDirectorySeparator(right),
            StringComparison.OrdinalIgnoreCase);

    public void Dispose()
    {
        Previews.Changed -= OnPreviewsChanged;
        _timer?.Dispose();
        _timer = null;
    }
}
