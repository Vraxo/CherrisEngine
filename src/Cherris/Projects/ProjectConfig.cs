using YamlDotNet.Serialization;
using YamlDotNet.Serialization.NamingConventions;

namespace Cherris.Projects;

/// <summary>
/// The <c>project.yaml</c> descriptor of a project folder: what it is called and which scene to open.
/// </summary>
/// <remarks>
/// The on-disk format is shared with the ImGui editor (<c>src/CherrisEditor/Projects/Project.cs</c>), which
/// writes the same PascalCase keys, so a project authored in one front end opens in the other.
/// </remarks>
public sealed class ProjectConfig
{
    /// <summary>File name expected at the root of every project folder.</summary>
    public const string FileName = "project.yaml";

    /// <summary>Default names for a freshly created project.</summary>
    public const string DefaultStartScene = "Main.yaml";

    public string Name { get; set; } = "MyGame";

    public string StartScene { get; set; } = DefaultStartScene;

    public bool PackAssets { get; set; }

    /// <summary>Absolute path of the project folder. Not part of the file.</summary>
    [YamlIgnore]
    public string RootPath { get; set; } = string.Empty;

    /// <summary>Absolute path of <c>project.yaml</c>.</summary>
    [YamlIgnore]
    public string ConfigPath => Path.Combine(RootPath, FileName);

    /// <summary>Absolute path of the start scene.</summary>
    [YamlIgnore]
    public string StartScenePath => Path.Combine(RootPath, StartScene);

    /// <summary>
    /// Reads the descriptor of an existing project folder. A folder without <c>project.yaml</c> yields the
    /// defaults, so the caller can decide whether that is acceptable.
    /// </summary>
    public static ProjectConfig Load(string projectRoot)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(projectRoot);

        string root = Path.GetFullPath(projectRoot);
        string configPath = Path.Combine(root, FileName);

        if (!File.Exists(configPath))
        {
            return new ProjectConfig { RootPath = root };
        }

        IDeserializer deserializer = new DeserializerBuilder()
            .WithNamingConvention(PascalCaseNamingConvention.Instance)
            .IgnoreUnmatchedProperties()
            .Build();

        ProjectConfig project = deserializer.Deserialize<ProjectConfig>(File.ReadAllText(configPath))
            ?? new ProjectConfig();

        project.RootPath = root;

        if (string.IsNullOrWhiteSpace(project.StartScene))
        {
            project.StartScene = DefaultStartScene;
        }

        return project;
    }

    /// <summary>Writes <c>project.yaml</c>, creating the folder when needed.</summary>
    public void Save()
    {
        if (string.IsNullOrWhiteSpace(RootPath))
        {
            throw new InvalidOperationException("A project needs a root path before it can be saved.");
        }

        Directory.CreateDirectory(RootPath);

        ISerializer serializer = new SerializerBuilder()
            .WithNamingConvention(PascalCaseNamingConvention.Instance)
            .Build();

        File.WriteAllText(ConfigPath, serializer.Serialize(this));
    }

    /// <summary>Creates an empty start scene when the project does not have one yet.</summary>
    public void CreateStartSceneIfNeeded()
    {
        string scenePath = StartScenePath;

        if (File.Exists(scenePath))
        {
            return;
        }

        string? directory = Path.GetDirectoryName(scenePath);

        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }

        File.WriteAllText(scenePath, "GameObjects: []");
    }
}
