using YamlDotNet.Serialization;
using YamlDotNet.Serialization.NamingConventions;

namespace CherrisEditor;

public class Project
{
    public string Name { get; set; } = "MyGame";
    public string StartScene { get; set; } = "Main.yaml";

    [YamlIgnore]
    public string RootPath { get; set; } = string.Empty;

    [YamlIgnore]
    public string ConfigPath => Path.Combine(RootPath, "project.yaml");

    public static Project Load(string projectRoot)
    {
        string configPath = Path.Combine(projectRoot, "project.yaml");

        if (!File.Exists(configPath))
        {
            return new Project { RootPath = projectRoot };
        }

        var deserializer = new DeserializerBuilder()
            .WithNamingConvention(PascalCaseNamingConvention.Instance)
            .Build();

        var yaml = File.ReadAllText(configPath);
        var project = deserializer.Deserialize<Project>(yaml);
        project.RootPath = projectRoot;
        return project;
    }

    public void Save()
    {
        var serializer = new SerializerBuilder()
            .WithNamingConvention(PascalCaseNamingConvention.Instance)
            .Build();

        string yaml = serializer.Serialize(this);
        File.WriteAllText(ConfigPath, yaml);
    }

    public void CreateSceneIfNeeded()
    {
        string scenePath = Path.Combine(RootPath, StartScene);
        if (!File.Exists(scenePath))
        {
            _ = Directory.CreateDirectory(Path.GetDirectoryName(scenePath)!);
            File.WriteAllText(scenePath, "GameObjects: []");
        }
    }
}