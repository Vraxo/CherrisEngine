namespace Cherris.Scripting;

/// <summary>The kinds of script a project can hold: what decides the file, the extension and the source.</summary>
public enum ScriptKind
{
    /// <summary>A Lua file the engine's script adapter loads when the scene runs.</summary>
    Lua,

    /// <summary>
    /// A C# file the editor compiles into the game assembly. A scene names it by class name, the way it names a
    /// built-in component, because it becomes one.
    /// </summary>
    CSharp
}

/// <summary>
/// The script the editor creates from a typed name: where such a script lives, which names are usable, and the
/// source it starts with — for either language a project may use.
/// </summary>
/// <remarks>
/// <para>
/// This is the only place that knows the <c>Scripts/</c> convention, so the editor, the command and
/// <see cref="Components.LanguageScript"/> cannot drift apart on it.
/// </para>
/// <para>
/// The two kinds differ in more than an extension, which is why the kind is a parameter rather than a guess from
/// the text. A Lua file is data the adapter reads at run time and the editor never compiles. A C# file is source
/// the editor compiles: its class name is what a scene stores, and until that compile has run the type is not
/// something the scene can name.
/// </para>
/// </remarks>
public static class ScriptTemplate
{
    /// <summary>Project-relative folder scripts live in; <see cref="Components.LanguageScript"/> refuses others.</summary>
    public const string Folder = "Scripts";

    /// <summary>Extension of a Lua script.</summary>
    public const string LuaExtension = ".lua";

    /// <summary>Extension of a C# script.</summary>
    public const string CSharpExtension = ".cs";

    /// <summary>Language adapter name <see cref="Components.LanguageScript"/> looks up for a Lua script.</summary>
    public const string LuaLanguage = "lua";

    /// <summary>Longest name accepted, which keeps the file name inside what file systems allow.</summary>
    public const int MaximumNameLength = 64;

    /// <summary>Both extensions, so a typed name is forgiven whichever language it was typed for.</summary>
    private static readonly string[] Extensions = [LuaExtension, CSharpExtension];

    /// <summary>The extension a script of this kind is written with, <c>.lua</c> or <c>.cs</c>.</summary>
    /// <param name="kind">The language.</param>
    public static string ExtensionOf(ScriptKind kind) =>
        kind == ScriptKind.CSharp ? CSharpExtension : LuaExtension;

    /// <summary>The name the language calls itself in the editor, for a menu or a choice row.</summary>
    /// <param name="kind">The language.</param>
    public static string DisplayName(ScriptKind kind) => kind == ScriptKind.CSharp ? "C#" : "Lua";

    /// <summary>True when the name can be a script: a letter or underscore, then letters, digits or underscores.</summary>
    /// <param name="name">Name without a folder or extension.</param>
    public static bool IsValidName(string? name)
    {
        if (String.IsNullOrWhiteSpace(name) || name.Length > MaximumNameLength)
        {
            return false;
        }

        if (!Char.IsAsciiLetter(name[0]) && name[0] != '_')
        {
            return false;
        }

        return name.All(character => Char.IsAsciiLetterOrDigit(character) || character == '_');
    }

    /// <summary>
    /// The name a typed search text would create, or null when it could not name a script. Surrounding space is
    /// dropped, and a typed extension of either language is forgiven, so " Player.lua " and "Player.cs" both mean
    /// <c>Player</c>.
    /// </summary>
    /// <param name="typed">What the user typed.</param>
    public static string? Normalize(string? typed)
    {
        string name = (typed ?? string.Empty).Trim();

        foreach (string extension in Extensions)
        {
            if (name.EndsWith(extension, StringComparison.OrdinalIgnoreCase))
            {
                name = name[..^extension.Length];
                break;
            }
        }

        return IsValidName(name) ? name : null;
    }

    /// <summary>Project-relative path of the script: <c>Scripts/Name.lua</c> or <c>Scripts/Name.cs</c>.</summary>
    /// <param name="name">A valid, normalized script name.</param>
    /// <param name="kind">The language, which decides the extension.</param>
    public static string RelativePath(string name, ScriptKind kind) => $"{Folder}/{name}{ExtensionOf(kind)}";

    /// <summary>Absolute path inside a project folder, or null when there is no project or no usable name.</summary>
    /// <param name="projectRoot">Absolute folder of the open project.</param>
    /// <param name="typed">What the user typed.</param>
    /// <param name="kind">The language, which decides the extension.</param>
    public static string? FullPath(string? projectRoot, string? typed, ScriptKind kind) =>
        String.IsNullOrWhiteSpace(projectRoot) || Normalize(typed) is not { } name
            ? null
            : Path.Combine(projectRoot, Folder, name + ExtensionOf(kind));

    /// <summary>True when the open project already holds a script of this kind with this name.</summary>
    /// <param name="projectRoot">Absolute folder of the open project.</param>
    /// <param name="typed">What the user typed.</param>
    /// <param name="kind">The language, which decides which file is looked for.</param>
    public static bool Exists(string? projectRoot, string? typed, ScriptKind kind) =>
        FullPath(projectRoot, typed, kind) is { } path && File.Exists(path);

    /// <summary>
    /// The source a new script starts with: the handlers the language expects, and a line saying where the file
    /// came from.
    /// </summary>
    /// <param name="name">A valid script name.</param>
    /// <param name="kind">The language the source is written in.</param>
    public static string Source(string name, ScriptKind kind) =>
        kind == ScriptKind.CSharp ? CSharpSource(name) : LuaSource(name);

    private static string LuaSource(string name) =>
        $"""
        -- {name}.lua — added from the Inspector's Add Component menu.
        -- start() runs once when the scene starts, update(deltaTime) runs every frame.

        function start()
        end

        function update(deltaTime)
        end

        """;

    /// <summary>
    /// A C# script that is a component: the class name is the file name, which is what the compiler registers and
    /// what the scene will store.
    /// </summary>
    /// <remarks>
    /// Only <c>Cherris.Components</c> is imported, because that is where <c>Script</c> lives; the older template
    /// imported <c>Cherris</c>, which does not resolve it and left the new file with one error before it was
    /// edited.
    /// </remarks>
    private static string CSharpSource(string name) =>
        $$"""
        // {{name}}.cs — added from the Inspector's Add Component menu.
        // Start() runs once when the scene starts, Update(deltaTime) runs every frame.

        using Cherris.Components;

        public class {{name}} : Script
        {
            public override void Start()
            {
            }

            public override void Update(float deltaTime)
            {
            }
        }

        """;

    /// <summary>
    /// Writes the script into the project, unless it is already there: an existing file is never overwritten,
    /// because it may hold work the user did in another editor.
    /// </summary>
    /// <param name="projectRoot">Absolute folder of the open project.</param>
    /// <param name="typed">What the user typed.</param>
    /// <param name="kind">The language the file is written in.</param>
    /// <param name="relativePath">The project-relative path of the script, as the component stores it.</param>
    /// <param name="error">Why nothing was written, or null.</param>
    /// <returns>True when the script is on disk.</returns>
    public static bool TryCreate(
        string? projectRoot,
        string? typed,
        ScriptKind kind,
        out string relativePath,
        out string? error)
    {
        relativePath = string.Empty;

        if (Normalize(typed) is not { } name)
        {
            error = $"'{typed}' cannot be a script name: start with a letter or underscore, then use letters, " +
                "digits or underscores.";
            return false;
        }

        if (FullPath(projectRoot, name, kind) is not { } path)
        {
            error = "no project folder is open, so there is nowhere to put the script.";
            return false;
        }

        try
        {
            if (!File.Exists(path))
            {
                Directory.CreateDirectory(Path.GetDirectoryName(path)!);
                File.WriteAllText(path, Source(name, kind));
            }

            relativePath = RelativePath(name, kind);
            error = null;
            return true;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            error = $"could not write {RelativePath(name, kind)}: {exception.Message}";
            return false;
        }
    }
}
