namespace Cherris.Scripting;

/// <summary>
/// The lua script the editor creates from a typed name: where such a script lives, which names are usable, and
/// the source it starts with.
/// </summary>
/// <remarks>
/// This is the only place that knows the <c>Scripts/</c> convention, so the editor, the command and
/// <see cref="Components.LanguageScript"/> cannot drift apart on it.
/// </remarks>
public static class ScriptTemplate
{
    /// <summary>Project-relative folder scripts live in; <see cref="Components.LanguageScript"/> refuses others.</summary>
    public const string Folder = "Scripts";

    /// <summary>Extension the editor writes.</summary>
    public const string Extension = ".lua";

    /// <summary>Language adapter name <see cref="Components.LanguageScript"/> looks up.</summary>
    public const string Language = "lua";

    /// <summary>Longest name accepted, which keeps the file name inside what file systems allow.</summary>
    public const int MaximumNameLength = 64;

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
    /// dropped, and a typed <c>.lua</c> is forgiven, so " Player.lua " means <c>Player</c>.
    /// </summary>
    /// <param name="typed">What the user typed.</param>
    public static string? Normalize(string? typed)
    {
        string name = (typed ?? string.Empty).Trim();

        if (name.EndsWith(Extension, StringComparison.OrdinalIgnoreCase))
        {
            name = name[..^Extension.Length];
        }

        return IsValidName(name) ? name : null;
    }

    /// <summary>Project-relative path of the script: <c>Scripts/Name.lua</c>.</summary>
    /// <param name="name">A valid script name.</param>
    public static string RelativePath(string name) => $"{Folder}/{name}{Extension}";

    /// <summary>Absolute path inside a project folder, or null when there is no project or no usable name.</summary>
    /// <param name="projectRoot">Absolute folder of the open project.</param>
    /// <param name="typed">What the user typed.</param>
    public static string? FullPath(string? projectRoot, string? typed) =>
        String.IsNullOrWhiteSpace(projectRoot) || Normalize(typed) is not { } name
            ? null
            : Path.Combine(projectRoot, Folder, name + Extension);

    /// <summary>True when the open project already holds a script with this name.</summary>
    /// <param name="projectRoot">Absolute folder of the open project.</param>
    /// <param name="typed">What the user typed.</param>
    public static bool Exists(string? projectRoot, string? typed) =>
        FullPath(projectRoot, typed) is { } path && File.Exists(path);

    /// <summary>
    /// The source a new script starts with: the two handlers the lua adapter calls, and a line saying where the
    /// file came from.
    /// </summary>
    /// <param name="name">A valid script name.</param>
    public static string Source(string name) =>
        $"""
        -- {name}.lua — added from the Inspector's Add ▾ menu.
        -- start() runs once when the scene starts, update(deltaTime) runs every frame.

        function start()
        end

        function update(deltaTime)
        end

        """;

    /// <summary>
    /// Writes the script into the project, unless it is already there: an existing file is never overwritten,
    /// because it may hold work the user did in another editor.
    /// </summary>
    /// <param name="projectRoot">Absolute folder of the open project.</param>
    /// <param name="typed">What the user typed.</param>
    /// <param name="relativePath">The project-relative path of the script, as the component stores it.</param>
    /// <param name="error">Why nothing was written, or null.</param>
    /// <returns>True when the script is on disk.</returns>
    public static bool TryCreate(string? projectRoot, string? typed, out string relativePath, out string? error)
    {
        relativePath = string.Empty;

        if (Normalize(typed) is not { } name)
        {
            error = $"'{typed}' cannot be a script name: start with a letter or underscore, then use letters, " +
                "digits or underscores.";
            return false;
        }

        if (FullPath(projectRoot, name) is not { } path)
        {
            error = "no project folder is open, so there is nowhere to put the script.";
            return false;
        }

        try
        {
            if (!File.Exists(path))
            {
                Directory.CreateDirectory(Path.GetDirectoryName(path)!);
                File.WriteAllText(path, Source(name));
            }

            relativePath = RelativePath(name);
            error = null;
            return true;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            error = $"could not write {RelativePath(name)}: {exception.Message}";
            return false;
        }
    }
}
