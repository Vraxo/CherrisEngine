using Cherris.Components;
using Cherris.Core.Logging;
using Cherris.Utils;
using System.Runtime.Loader;

namespace Cherris.Core;

public sealed class Player : Engine
{
    private readonly string _projectRoot;

    public Player(string projectRoot) : base("Game", true, GraphicsAPI.OpenTK)
    {
        _projectRoot = projectRoot;
    }

    protected override void LoadContent()
    {
        ProjectFiles.ProjectRoot = _projectRoot;

        LoadCompiledScripts();
        LoadStartScene();
    }

    private void LoadCompiledScripts()
    {
        string assemblyPath = Path.Combine(_projectRoot, "GameScripts.dll");

        if (!File.Exists(assemblyPath))
        {
            Logger.Warning("[Player] No compiled scripts found. Running without game scripts.");
            return;
        }

        try
        {
            var context = new AssemblyLoadContext("GameScriptsPlayer", true);
            var assembly = context.LoadFromAssemblyPath(assemblyPath);

            foreach (var type in assembly.GetTypes())
            {
                if (typeof(Script).IsAssignableFrom(type) && !type.IsAbstract)
                {
                    SceneLoader.RegisterComponentFactory(type.Name, _ => (Script)Activator.CreateInstance(type)!);
                }
            }

            Logger.Info("[Player] Loaded compiled scripts from GameScripts.dll");
        }
        catch (Exception ex)
        {
            Logger.Error($"[Player] Failed to load scripts: {ex.Message}");
        }
    }

    private void LoadStartScene()
    {
        string configPath = Path.Combine(_projectRoot, "project.yaml");
        string scenePath = Path.Combine(_projectRoot, "Main.yaml");

        if (File.Exists(configPath))
        {
            foreach (var line in File.ReadLines(configPath))
            {
                var trimmed = line.Trim();
                if (trimmed.StartsWith("StartScene:"))
                {
                    var sceneName = trimmed.Replace("StartScene:", "").Trim();
                    scenePath = Path.Combine(_projectRoot, sceneName);
                    break;
                }
            }
        }

        if (!File.Exists(scenePath))
        {
            Logger.Error($"[Player] Start scene not found: {scenePath}");
            return;
        }

        var gameObjects = SceneLoader.LoadScene(scenePath);
        var scene = new Scene(scenePath, gameObjects);
        SceneManager.OpenScene(scene);
        SceneManager.Start();
    }

    protected override void Update(float deltaTime)
    {
        if (Input.WasKeyPressed(Key.Escape))
        {
            Environment.Exit(0);
        }

        SceneManager.Update(deltaTime, true);
    }
}