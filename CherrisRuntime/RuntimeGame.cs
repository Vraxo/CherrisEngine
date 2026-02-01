using Cherris;
using Cherris.Components;
using Cherris.Core;
using Cherris.Core.Logging;
using Cherris.Serialization;
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
        ResourceManager.LoadInitialAssets();

        // 1. Register internal components (Camera, Light, etc.)
        RegisterInternalComponents();

        // 2. Load and register user scripts
        LoadGameScripts();

        // 3. Load project config and start scene
        LoadProjectAndScene();
    }

    private void RegisterInternalComponents()
    {
        // These are the same basic types the editor supports
        ComponentDeserializer.RegisterFactories(SceneLoader, ResourceManager);
    }

    private void LoadGameScripts()
    {
        string dllPath = Path.Combine(AppContext.BaseDirectory, GameScriptsDll);

        if (!File.Exists(dllPath))
        {
            Logger.Warning($"[Runtime] No scripts assembly found at '{dllPath}'.");
            return;
        }

        try
        {
            Assembly assembly = Assembly.LoadFrom(dllPath);
            var scriptTypes = assembly.GetTypes()
                .Where(t => typeof(Script).IsAssignableFrom(t) && !t.IsAbstract);

            foreach (Type type in scriptTypes)
            {
                // Register the factory for this script type
                SceneLoader.RegisterComponentFactory(type.Name, _ => (Component)Activator.CreateInstance(type)!);
                Logger.Info($"[Runtime] Registered script: {type.Name}");
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
        string startScenePath = "Assets/Scenes/Main.yaml"; // Default fallback

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
            catch (Exception ex)
            {
                Logger.Error($"[Runtime] Error reading project config: {ex.Message}");
            }
        }

        LoadScene(startScenePath);
    }

    private void LoadScene(string filePath)
    {
        string fullPath = Path.Combine(AppContext.BaseDirectory, filePath);

        if (!File.Exists(fullPath))
        {
            Logger.Error($"[Runtime] Start scene not found at: {fullPath}");
            return;
        }

        Logger.Info($"[Runtime] Loading scene: {filePath}");

        var gameObjects = SceneLoader.LoadScene(fullPath);
        var scene = new Scene(filePath, gameObjects);

        SceneManager.OpenScene(scene);
        SceneManager.Start();
    }
}