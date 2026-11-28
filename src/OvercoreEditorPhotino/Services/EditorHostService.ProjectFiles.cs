using Overcore.Core;
using Overcore.Core.Logging;
using Overcore.Projects;
using Overcore.Scripting;
using Overcore.Utils;
using OvercoreEditorSession;
using OvercoreEditorSession.Host;
using System.Net;
using System.Net.Sockets;
using System.Runtime.InteropServices;

namespace OvercoreEditorPhotino.Services;

// The project as files: opening one, the recent list, and creating scenes, scripts and folders inside it.
public sealed partial class EditorHostService : IDisposable
{
    /// <summary>Recent projects, most recent first, with their state resolved for display.</summary>
    public IReadOnlyList<RecentProjectEntry> RecentProjects => _recent.Get()
        .Select(path => new RecentProjectEntry(path, ProjectWorkspace.DisplayName(path), ProjectWorkspace.Exists(path)))
        .ToArray();

    /// <summary>
    /// Opens a project folder: validates it, remembers it in the shared history and starts a session that
    /// renders the project's start scene. Returns false when the project could not be opened; the reason is
    /// in <see cref="FailureMessage"/>.
    /// </summary>
    public async Task<bool> OpenProjectAsync(string projectRoot, bool rememberInHistory = true)
    {
        string root;
        ProjectConfig project;

        try
        {
            root = ProjectWorkspace.Validate(projectRoot);
            project = ProjectConfig.Load(root);
        }
        catch (Exception exception)
        {
            Fail($"Could not open that project: {exception.Message}", exception.ToString());
            return false;
        }

        if (rememberInHistory)
        {
            _recent.Remember(root);
        }

        WriteLog($"[editor] opening project '{project.Name}' at {root}");

        return await StartSessionAsync(EditorSceneRequest.FromProject(project), root).ConfigureAwait(true);
    }

    /// <summary>Starts a session that renders the built-in demo scene.</summary>
    public Task<bool> OpenDemoSceneAsync()
    {
        WriteLog("[editor] opening the built-in demo scene");

        return StartSessionAsync(EditorSceneRequest.Demo, null);
    }

    /// <summary>
    /// Asks the open session to load another scene file in place of the one it has. The session itself stays — the
    /// viewport keeps its connection and the panels keep polling — so this is a scene change, not a restart.
    /// </summary>
    /// <param name="scenePath">Absolute path of a scene file, normally inside the open project.</param>
    /// <returns>False when there is no session to ask; the reason is in <see cref="FailureMessage"/>.</returns>
    public bool OpenScene(string scenePath)
    {
        EditorSessionHost? host = _session;

        if (host is null)
        {
            // Not a failure of the session: there simply is not one. The menus that offer this need a session to be
            // enabled in the first place, so this is a race, not a state to report.
            WriteLog("[editor] there is no session to open a scene in", LogLevel.Warning);
            return false;
        }

        // The selection names an object in the scene that is going away, so it cannot survive the switch.
        SelectionSink?.Invoke(null);

        // Play mode belongs to the scene: the render thread stops it, and the toolbar has to say so too.
        _playState = EditorPlayState.Editing;

        if (!host.RequestSceneOpen(scenePath))
        {
            WriteLog("[editor] the session is shutting down, so the scene was not opened", LogLevel.Warning);
            return false;
        }

        WriteLog($"[editor] opening '{scenePath}'");

        return true;
    }

    /// <summary>
    /// Writes a new scene into the open project and opens it. The file is written first, and everything that can go
    /// wrong — no project, a name that is not a file name, a scene that already exists, a file that cannot be
    /// opened — comes back as text for the dialog to show, so nothing is created silently or left half-done.
    /// </summary>
    /// <param name="name">What the user typed; the extension is added when it is left off.</param>
    /// <param name="error">Why no scene was made, when this returns false.</param>
    public bool TryCreateScene(string name, out string? error) => CreateScene(name, null, out error) is not null;

    /// <summary>
    /// Writes a new scene into a folder of the open project and opens it.
    /// </summary>
    /// <param name="name">What the user typed; the extension is added when it is left off.</param>
    /// <param name="directory">Folder to write into, or null for the project root.</param>
    /// <param name="error">Why no scene was made, when this returns null.</param>
    /// <returns>The path of the file, or null when nothing was written.</returns>
    public string? CreateScene(string name, string? directory, out string? error)
    {
        if (!ResolveDirectory("a scene", directory, out string folder, out error))
        {
            return null;
        }

        string path;

        try
        {
            path = SceneTemplate.Write(folder, name);
        }
        catch (Exception exception) when (exception is ArgumentException or IOException or UnauthorizedAccessException)
        {
            error = exception.Message;
            return null;
        }

        WriteLog($"[editor] created '{path}'");

        if (!OpenScene(path))
        {
            // The file is on disk and says so; only opening it failed, so the dialog reports that rather than
            // pretending the scene was never made.
            error = $"'{Path.GetFileName(path)}' was created, but the session could not open it.";
            return null;
        }

        return path;
    }

    /// <summary>
    /// Writes a new script into the project's <c>Scripts/</c> folder, in either language.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The folder is not a choice: a script kept anywhere else is one the engine will never run, so a browser that
    /// let one be written into <c>Levels/</c> would be making a dead file.
    /// </para>
    /// <para>
    /// A C# file is compiled straight away. Nothing is attached here — the browser only made a file — but the Add
    /// Component menu should offer the type it declares as soon as the dialog closes, and it only knows types the
    /// session has compiled.
    /// </para>
    /// </remarks>
    /// <param name="name">What the user typed; the extension is added when it is left off.</param>
    /// <param name="kind">Lua or C#, which decides the extension and the source.</param>
    /// <param name="error">Why no script was made, when this returns null.</param>
    /// <returns>The path of the file, or null when nothing was written.</returns>
    public string? CreateScript(string name, ScriptKind kind, out string? error)
    {
        error = null;

        if (_projectRoot is not { } projectRoot)
        {
            error = "Open a project before making a script: a script is a file inside one.";
            return null;
        }

        if (!ScriptTemplate.TryCreate(projectRoot, name, kind, out string relativePath, out error))
        {
            return null;
        }

        if (kind == ScriptKind.CSharp)
        {
            _session?.RequestScriptReload();
        }

        string path = Path.Combine(projectRoot, relativePath.Replace('/', Path.DirectorySeparatorChar));

        WriteLog($"[editor] created '{path}'");

        return path;
    }

    /// <summary>
    /// Creates a folder inside the open project.
    /// </summary>
    /// <param name="name">What the user typed; a name, not a path.</param>
    /// <param name="directory">Folder to create it in, or null for the project root.</param>
    /// <param name="error">Why no folder was made, when this returns null.</param>
    /// <returns>The path of the folder, or null when nothing was created.</returns>
    public string? CreateFolder(string name, string? directory, out string? error)
    {
        if (!ProjectFileName.TryNormalize(name, out string folderName, out string nameError))
        {
            error = nameError;
            return null;
        }

        if (!ResolveDirectory("a folder", directory, out string parent, out error))
        {
            return null;
        }

        string path = Path.Combine(parent, folderName);

        if (Directory.Exists(path) || File.Exists(path))
        {
            error = $"There is already something called {folderName} here.";
            return null;
        }

        try
        {
            // A folder holds no contents of its own, so there is nothing to write, only somewhere to put things.
            Directory.CreateDirectory(path);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or ArgumentException)
        {
            error = $"Could not create {folderName}: {exception.Message}";
            return null;
        }

        WriteLog($"[editor] created '{path}'");

        return path;
    }

    /// <summary>
    /// The folder an item is to be created in: the project root when none was named, and never anything outside it.
    /// </summary>
    /// <param name="what">What is being created, for the messages.</param>
    /// <param name="directory">The folder asked for, or null.</param>
    /// <param name="folder">The absolute folder to write into, when this returns true.</param>
    /// <param name="error">Why the folder cannot be used, when this returns false.</param>
    private bool ResolveDirectory(string what, string? directory, out string folder, out string? error)
    {
        folder = string.Empty;
        error = null;

        if (_projectRoot is not { } projectRoot)
        {
            error = $"Open a project before making {what}: everything the editor writes lives inside one.";
            return false;
        }

        string root = Path.GetFullPath(projectRoot);

        if (string.IsNullOrWhiteSpace(directory))
        {
            folder = root;
            return true;
        }

        string candidate;

        try
        {
            candidate = Path.GetFullPath(directory);
        }
        catch (Exception exception) when (exception is ArgumentException or IOException or NotSupportedException)
        {
            error = $"'{directory}' is not a folder the editor can write to.";
            return false;
        }

        // The browser only ever shows one project's folder, but the path arrives from a click: it is checked here
        // rather than trusted, so a created file cannot land outside the project it belongs to.
        string prefix = root.EndsWith(Path.DirectorySeparatorChar)
            ? root
            : root + Path.DirectorySeparatorChar;

        if (!candidate.Equals(root, StringComparison.OrdinalIgnoreCase) &&
            !candidate.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
        {
            error = "Files can only be created inside the open project.";
            return false;
        }

        folder = candidate;

        return true;
    }

    /// <summary>A free file name in the folder: <c>Cube.prefab</c>, then <c>Cube 1.prefab</c>, and so on.</summary>
    private static string UniqueAssetPath(string projectRoot, string baseName)
    {
        string name = string.Join("_", baseName.Split(Path.GetInvalidFileNameChars()));
        string candidate = Path.Combine(projectRoot, name + ".prefab");

        for (int i = 1; File.Exists(candidate) || File.Exists(candidate + AssetMeta.Suffix); i++)
        {
            candidate = Path.Combine(projectRoot, name + " " + i + ".prefab");
        }

        return candidate;
    }

    /// <summary>Removes a project from the launcher's history without touching its files.</summary>
    public void ForgetProject(string projectRoot)
    {
        try
        {
            _recent.Forget(projectRoot);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            WriteLog($"[editor] could not update the recent projects: {exception.Message}");
        }
    }
}
