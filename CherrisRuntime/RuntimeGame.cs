using Cherris;
using Cherris.Components;
using Cherris.Core;
using Cherris.Core.Logging;
using Cherris.Serialization;
using Cherris.Utils;
using System.Reflection;
using YamlDotNet.Serialization;
using YamlDotNet.Serialization.NamingConventions;

namespace CherrisRuntime;

public class RuntimeGame : Engine
{
    private const string GameScriptsDll = "GameScripts.dll";
    private const string ConfigFile = "project.yaml";

    public RuntimeGame() : base("Cherris Game", true, GraphicsAPI.OpenTK)
    {
    }

    protected override void LoadContent()
    {
        // Initialize Virtual File System
        ProjectFiles.Initialize(AppContext.BaseDirectory);

        ResourceManager.LoadInitialAssets();
        RegisterInternalComponents();
        LoadGameScripts();
        LoadProjectAndScene();
    }

    private void RegisterInternalComponents()
    {
        ComponentDeserializer.RegisterFactories(SceneLoader, ResourceManager);
    }

    private void LoadGameScripts()
    {
        string dllPath = Path.Combine(AppContext.BaseDirectory, GameScriptsDll);

        if (!File.Exists(dllPath))
        {
            return;
        }

        try
        {
            Assembly assembly = Assembly.LoadFrom(dllPath);
            var scriptTypes = assembly.GetTypes()
                .Where(t => typeof(Script).IsAssignableFrom(t) && !t.IsAbstract);

            foreach (Type type in scriptTypes)
            {
                SceneLoader.RegisterComponentFactory(type.Name, _ => (Component)Activator.CreateInstance(type)!);
            }
        }
        catch (Exception ex)
        {
            Logger.Error($"[Runtime] Failed to load game scripts: {ex.Message}");
        }
    }

    private void LoadProjectAndScene()
    {
        string configPath = Path.Combine(AppContext.BaseDirectory, ConfigFile);
        string startScenePath = "Scenes/Main.yaml";

        if (File.Exists(configPath))
        {
            try
            {
                var deserializer = new DeserializerBuilder()
                    .WithNamingConvention(PascalCaseNamingConvention.Instance)
                    .Build();

                var config = deserializer.Deserialize<Dictionary<string, string>>(File.ReadAllText(configPath));

                if (config.TryGetValue("StartScene", out string? configuredScene) && !string.IsNullOrEmpty(configuredScene))
                {
                    startScenePath = configuredScene;
                }
            }
            catch { }
        }

        // ProjectFiles.Open finds it in pack or disk
        Logger.Info($"[Runtime] Loading scene: {startScenePath}");
        var gameObjects = SceneLoader.LoadScene(startScenePath);
        var scene = new Scene(startScenePath, gameObjects);
        SceneManager.OpenScene(scene);
        SceneManager.Start();
    }
}