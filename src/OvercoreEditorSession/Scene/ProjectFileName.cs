namespace OvercoreEditorSession;

/// <summary>
/// Names for the things the editor writes into a project — scenes, scripts, folders — and which of them are usable.
/// </summary>
/// <remarks>
/// One rule for all of them, and the same rule on every platform: a project is usually committed and opened
/// somewhere else, so a file called <c>Level:1</c> would only reveal itself as a problem on the machine that cannot
/// write it. The rules this engine's editor applies to the names it makes are not the operating system's.
/// </remarks>
public static class ProjectFileName
{
    /// <summary>
    /// Characters a name may not contain: the ones Windows refuses, which is the strictest of the systems a project
    /// may be opened on.
    /// </summary>
    private static readonly char[] InvalidChars = ['\\', '/', ':', '*', '?', '"', '<', '>', '|'];

    /// <summary>What the names may not hold, in the words a dialog says them.</summary>
    public const string InvalidCharsMessage = "A name cannot contain a path or any of \\ / : * ? \" < > |.";

    /// <summary>
    /// Trims a typed name and checks it could be a file or folder name inside the project.
    /// </summary>
    /// <param name="typed">What the user typed.</param>
    /// <param name="name">The name to use, trimmed, when this returns true.</param>
    /// <param name="error">Why the name cannot be used, when this returns false.</param>
    public static bool TryNormalize(string? typed, out string name, out string error)
    {
        name = string.Empty;
        error = string.Empty;

        string trimmed = (typed ?? string.Empty).Trim();

        if (trimmed.Length == 0)
        {
            error = "Type a name first.";
            return false;
        }

        // A dot at the front hides the file on Unix and macOS, and a path separator would put it outside the folder
        // the editor thinks it is writing into.
        if (trimmed.StartsWith('.') || trimmed.IndexOfAny(InvalidChars) >= 0 || trimmed.Any(char.IsControl))
        {
            error = InvalidCharsMessage;
            return false;
        }

        name = trimmed;

        return true;
    }

    /// <summary>
    /// The name with <paramref name="extension"/> on the end, unless it already carries it — or the other spelling
    /// scenes are saved with, so a typed <c>.yml</c> is kept rather than turned into <c>.yml.yaml</c>.
    /// </summary>
    /// <param name="name">A name <see cref="TryNormalize"/> accepted.</param>
    /// <param name="extension">The extension to add, dot included; empty for folders.</param>
    public static string WithExtension(string name, string extension)
    {
        if (extension.Length == 0 ||
            name.EndsWith(extension, StringComparison.OrdinalIgnoreCase) ||
            (extension == ".yaml" && name.EndsWith(".yml", StringComparison.OrdinalIgnoreCase)))
        {
            return name;
        }

        return name + extension;
    }

    /// <summary>
    /// The first name based on <paramref name="baseName"/> that is free in <paramref name="directory"/>, so a dialog
    /// can open on something that will work rather than on a refusal.
    /// </summary>
    /// <param name="directory">Folder to look in; a missing folder is treated as empty.</param>
    /// <param name="baseName">The name to start from, without its extension.</param>
    /// <param name="extension">The extension to add, dot included; empty for folders.</param>
    public static string Available(string directory, string baseName, string extension)
    {
        for (int index = 1; index < 1000; index++)
        {
            string candidate = index == 1 ? baseName : baseName + index;

            if (!IsTaken(directory, candidate + extension))
            {
                return candidate;
            }
        }

        return baseName;
    }

    /// <summary>
    /// The absolute path a name becomes in a folder, for a dialog to show before anything is written.
    /// </summary>
    /// <param name="directory">The folder the item would be created in.</param>
    /// <param name="typed">What the user typed so far.</param>
    /// <param name="extension">The extension to add, dot included; empty for folders.</param>
    /// <param name="path">The path, when the name is usable.</param>
    /// <returns>False when the typed name is not one the editor would write, so there is no path to show.</returns>
    public static bool TryPreview(string? directory, string? typed, string extension, out string path)
    {
        path = string.Empty;

        if (string.IsNullOrWhiteSpace(directory) || !TryNormalize(typed, out string name, out _))
        {
            return false;
        }

        path = Path.Combine(directory, WithExtension(name, extension));

        return true;
    }

    /// <summary>The path, said the way a dialog says it: relative to the project when it is inside one.</summary>
    /// <param name="path">Absolute path of the file or folder.</param>
    /// <param name="projectRoot">Absolute folder of the open project, or null.</param>
    public static string Describe(string path, string? projectRoot)
    {
        if (string.IsNullOrWhiteSpace(projectRoot) || string.IsNullOrEmpty(path))
        {
            return path;
        }

        try
        {
            string relative = Path.GetRelativePath(projectRoot, path);

            return relative.StartsWith("..", StringComparison.Ordinal)
                ? path
                : relative.Replace(Path.DirectorySeparatorChar, '/');
        }
        catch (Exception exception) when (exception is ArgumentException or IOException or NotSupportedException)
        {
            return path;
        }
    }

    private static bool IsTaken(string directory, string fileName)
    {
        if (string.IsNullOrWhiteSpace(directory))
        {
            return false;
        }

        string path = Path.Combine(directory, fileName);

        return File.Exists(path) || Directory.Exists(path);
    }
}
