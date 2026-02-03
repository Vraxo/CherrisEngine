using Cherris.Components;
using Cherris.Core.Logging;
using System.Numerics;

namespace Cherris.Core;

public class Scene : IDisposable
{
    public Guid Id { get; } = Guid.NewGuid();
    public string Name { get; set; }
    public string FilePath { get; set; }
    public List<GameObject> GameObjects { get; } = [];
    public List<Light> Lights { get; } = [];
    public Camera? MainCamera { get; set; } = null;
    public Skybox? Skybox { get; private set; } = null;
    public bool IsDirty { get; set; }

    public Scene(string filePath, List<GameObject> gameObjects)
    {
        FilePath = filePath;

        Name = string.IsNullOrEmpty(filePath)
            ? "Untitled Scene"
            : Path.GetFileName(filePath);

        GameObjects.AddRange(gameObjects);
        FindMainComponents();
    }

    public void AddGameObject(GameObject go)
    {
        GameObjects.Add(go);
        var light = go.GetComponent<Light>();

        if (light is not null)
        {
            Lights.Add(light);
        }

        IsDirty = true;
    }

    public void RemoveGameObject(GameObject go)
    {
        // Recursively remove children first to avoid modifying collection during iteration
        foreach (Transform? childTransform in go.Transform.Children.ToList())
        {
            RemoveGameObject(childTransform.GameObject);
        }

        // Remove the object from its parent's list
        go.Transform.Parent = null;

        // Remove from the root scene list
        GameObjects.Remove(go);

        // Remove light from cached list
        var light = go.GetComponent<Light>();

        if (light is not null)
        {
            Lights.Remove(light);
        }

        // Dispose its managed resources
        go.GetComponent<MeshRenderer>()?.Dispose();
        IsDirty = true;
    }

    public void Start(PhysicsSystem physicsSystem, AudioSystem audioSystem)
    {
        FindMainComponents();

        // Initialize scripts and components
        foreach (GameObject gameObject in GameObjects)
        {
            foreach (var script in gameObject.GetComponents<Script>())
            {
                if (script is RigidBody rb)
                {
                    rb.Initialize(physicsSystem);
                }

                try
                {
                    script.Start();
                }
                catch (Exception ex)
                {
                    Logger.Error($"[Scene] Exception in {script.GetType().Name}.Start() on '{gameObject.Name}': {ex.Message}\n{ex.StackTrace}");
                    script.Enabled = false;
                }
            }

            foreach (var audioSource in gameObject.GetComponents<AudioSource>())
            {
                audioSource.Initialize(audioSystem);
                audioSource.Start();
            }
        }

        // Handle case where no camera was found in the scene
        if (MainCamera is not null)
        {
            return;
        }

        CreateDefaultCamera();
    }

    public void Update(float deltaTime)
    {
        foreach (var gameObject in GameObjects)
        {
            foreach (var script in gameObject.GetComponents<Script>())
            {
                if (!script.Enabled)
                {
                    continue;
                }

                try
                {
                    script.Update(deltaTime);
                }
                catch (Exception ex)
                {
                    Logger.Error($"[Scene] Exception in {script.GetType().Name}.Update() on '{gameObject.Name}': {ex.Message}");
                    script.Enabled = false; // Disable the script to prevent console flooding
                }
            }
        }
    }

    public void FindMainComponents()
    {
        MainCamera = null;
        Skybox = null;
        Lights.Clear();

        foreach (GameObject gameObject in GameObjects)
        {
            MainCamera ??= gameObject.GetComponent<Camera>();
            Skybox ??= gameObject.GetComponent<Skybox>();
            var light = gameObject.GetComponent<Light>();

            if (light is not null)
            {
                Lights.Add(light);
            }
        }
    }

    private void CreateDefaultCamera()
    {
        Console.WriteLine("Warning: No active camera found in scene. Creating a default one.");
        GameObject go = new("Default Camera");
        go.Transform.Position = new Vector3(0, 1, 3);
        MainCamera = go.AddComponent(new Camera());
        GameObjects.Add(go);
    }

    public void Dispose()
    {
        foreach (GameObject gameObject in GameObjects)
        {
            gameObject.GetComponent<MeshRenderer>()?.Dispose();
        }
    }
}