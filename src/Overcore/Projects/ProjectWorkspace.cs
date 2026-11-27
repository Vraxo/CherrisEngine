namespace Overcore.Projects;

/// <summary>Creates and validates project folders. Errors carry text meant for the user, not the log.</summary>
public static class ProjectWorkspace
{
    /// <summary>
    /// Resolves and validates a project folder, so a typo cannot reach the renderer.
    /// </summary>
    /// <exception cref="ArgumentException">No path was given.</exception>
    /// <exception cref="DirectoryNotFoundException">The folder does not exist.</exception>
    /// <exception cref="InvalidOperationException">The folder is not a project (no <c>project.yaml</c>).</exception>
    public static string Validate(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            throw new ArgumentException("Choose a project folder.");
        }

        string root = Path.GetFullPath(path.Trim());

        if (!Directory.Exists(root))
        {
            throw new DirectoryNotFoundException("The project folder does not exist.");
        }

        if (!File.Exists(Path.Combine(root, ProjectConfig.FileName)))
        {
            throw new InvalidOperationException(
                $"No {ProjectConfig.FileName} was found in that folder, so it is not a Overcore project.");
        }

        return root;
    }

    /// <summary>Resolves the folder a new project would occupy, without creating anything.</summary>
    /// <exception cref="ArgumentException">The name or location is unusable.</exception>
    /// <exception cref="DirectoryNotFoundException">The location does not exist.</exception>
    public static string GetNewProjectPath(string location, string name)
    {
        if (string.IsNullOrWhiteSpace(name) || name != name.Trim() || name is "." or ".." ||
            name.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0 || name.Any(char.IsControl) ||
            name.Contains('/') || name.Contains('\\'))
        {
            throw new ArgumentException("Enter a valid folder name for the project.");
        }

        if (string.IsNullOrWhiteSpace(location))
        {
            throw new ArgumentException("Choose a location for the project.");
        }

        string parent = Path.GetFullPath(location.Trim());

        if (!Directory.Exists(parent))
        {
            throw new DirectoryNotFoundException("The selected location does not exist.");
        }

        return Path.Combine(parent, name);
    }

    /// <summary>Creates a project folder with <c>project.yaml</c> and an empty start scene.</summary>
    /// <exception cref="IOException">A file or folder with that name already exists.</exception>
    public static string Create(string location, string name)
    {
        string root = GetNewProjectPath(location, name);

        if (Directory.Exists(root) || File.Exists(root))
        {
            throw new IOException(
                "A file or folder with that name already exists. Choose another name or location.");
        }

        Directory.CreateDirectory(root);

        ProjectConfig project = new() { Name = name, RootPath = root };
        project.Save();
        project.CreateStartSceneIfNeeded();

        return root;
    }

    /// <summary>Creates a project by copying a built-in template into a new, user-owned folder.</summary>
    /// <remarks>The template source is never edited; the destination must not already exist.</remarks>
    public static string CreateFromTemplate(string location, string name, string templateId)
    {
        ProjectTemplate template = ProjectTemplateCatalog.Find(templateId)
            ?? throw new ArgumentException("Choose one of the available project templates.", nameof(templateId));
        if (template.IsBlank) return Create(location, name);

        string root = GetNewProjectPath(location, name);
        if (Directory.Exists(root) || File.Exists(root))
        {
            throw new IOException("A file or folder with that name already exists. Choose another name or location.");
        }

        string source = ProjectTemplateCatalog.GetSourceDirectory(template);
        string parent = Path.GetDirectoryName(root)!;
        string staging = Path.Combine(parent, $".overcore-template-{Guid.NewGuid():N}");
        Directory.CreateDirectory(staging);
        try
        {
            foreach (string directory in Directory.EnumerateDirectories(source, "*", SearchOption.AllDirectories))
            {
                Directory.CreateDirectory(Path.Combine(staging, Path.GetRelativePath(source, directory)));
            }
            foreach (string file in Directory.EnumerateFiles(source, "*", SearchOption.AllDirectories))
            {
                string target = Path.Combine(staging, Path.GetRelativePath(source, file));
                Directory.CreateDirectory(Path.GetDirectoryName(target)!);
                File.Copy(file, target, overwrite: false);
            }

            ProjectConfig project = ProjectConfig.Load(staging);
            project.Name = name;
            project.RootPath = staging;
            project.Save();
            project.CreateStartSceneIfNeeded();

            // Same-volume directory move is atomic: if another process claimed the target after validation, fail
            // rather than merging into or deleting that user's folder.
            Directory.Move(staging, root);
            return root;
        }
        catch
        {
            if (Directory.Exists(staging))
            {
                try { Directory.Delete(staging, recursive: true); }
                catch (Exception exception) when (exception is IOException or UnauthorizedAccessException) { }
            }
            throw;
        }
    }

    /// <summary>
    /// Name to show for a project folder. Prefers the name in <c>project.yaml</c> and falls back to the
    /// folder name, so a broken descriptor still produces a usable entry in the launcher.
    /// </summary>
    public static string DisplayName(string projectRoot)
    {
        string folderName = Path.GetFileName(Path.TrimEndingDirectorySeparator(projectRoot));

        try
        {
            string? name = ProjectConfig.Load(projectRoot).Name;

            if (!string.IsNullOrWhiteSpace(name))
            {
                return name;
            }
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or
            YamlDotNet.Core.YamlException)
        {
            // A malformed project.yaml should not hide the project; the folder name is still useful.
        }

        return string.IsNullOrEmpty(folderName) ? projectRoot : folderName;
    }

    /// <summary>True when the folder still looks like a project; used to flag dead entries as missing.</summary>
    public static bool Exists(string projectRoot) =>
        Directory.Exists(projectRoot) && File.Exists(Path.Combine(projectRoot, ProjectConfig.FileName));
}
