namespace OvercoreEditor;

/// <summary>Filesystem operations shared by the project launcher and its tests.</summary>
public static class ProjectLauncher
{
    public static string ValidateExistingProject(string path)
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

        if (!File.Exists(Path.Combine(root, "project.yaml")))
        {
            throw new InvalidOperationException("No project.yaml was found in that folder.");
        }

        return root;
    }

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

    public static string CreateProject(string location, string name)
    {
        string root = GetNewProjectPath(location, name);
        if (Directory.Exists(root) || File.Exists(root))
        {
            throw new IOException("A file or folder with that name already exists. Choose another name or location.");
        }

        Directory.CreateDirectory(root);
        var project = new Project { Name = name, RootPath = root };
        project.Save();
        project.CreateSceneIfNeeded();
        return root;
    }
}
