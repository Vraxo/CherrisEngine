using Cherris;
using Cherris.Core;
using Cherris.Core.Logging;
using Cherris.Rendering.OpenTK;
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
        // Enable direct-to-screen presentation for the Runtime
        if (Renderer is OpenTKRenderer otkRenderer)
        {
            otkRenderer.PresentToScreen = true;
            // Hide debugging visualizations in the shipping game
            otkRenderer.ShowGrid = false;
            otkRenderer.ShowPhysicsColliders = false;
        }
    }

    protected override void LoadContent()
    {
        ResourceManager.LoadInitialAssets();

        // 1. Load User Scripts (Assembly only, registration happens in Step 2 via deserializer lookup)
        LoadGameScriptsAssembly();

        // 2. Register internal components + generic "Script" handling
        RegisterInternalComponents();

        // 3. Load project config and start scene
        LoadProjectAndScene();
    }

    private void RegisterInternalComponents()
    {
        ComponentDeserializer.RegisterFactories(SceneLoader, ResourceManager);
    }

    private void LoadGameScriptsAssembly()
    {
        string dllPath = Path.Combine(AppContext.BaseDirectory, GameScriptsDll);

        if (!File.Exists(dllPath))
        {
            Logger.Warning($"[Runtime] No scripts assembly found at '{dllPath}'.");
            return;
        }

        try
        {
            // Just loading into the AppDomain is enough for ComponentDeserializer.FindTypeInAssemblies to find them
            Assembly.LoadFrom(dllPath);
            Logger.Info($"[Runtime] Loaded game assembly: {GameScriptsDll}");
        }
        catch (Exception ex)
        {
            Logger.Error($"[Runtime] Failed to load game scripts: {ex.Message}");
        }
    }

    private void LoadProjectAndScene()
    {
        string configPath = Path.Combine(AppContext.BaseDirectory, ConfigFile);
        string startScenePath = "Assets/Scenes/Main.yaml";

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