using Cherris.Components;
using Cherris.Core.Logging;
using Cherris.Core.Physics;
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

    // Kept from the last Start(): a script switched on later is started on the frame it is, with the same systems.
    private PhysicsSystem? _physicsSystem;

    private float _debugLogTimer;

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

        Light? light = go.GetComponent<Light>();

        if (light is not null)
        {
            Lights.Add(light);
        }

        IsDirty = true;
    }

    public void RemoveGameObject(GameObject go)
    {
        foreach (Transform? childTransform in go.Transform.Children.ToList())
        {
            RemoveGameObject(childTransform.GameObject);
        }

        go.Transform.Parent = null;
        GameObjects.Remove(go);

        Light? light = go.GetComponent<Light>();
        
        if (light is not null)
        {
            Lights.Remove(light);
        }

        go.GetComponent<MeshRenderer>()?.Dispose();
        IsDirty = true;
    }

    /// <summary>
    /// Starts every script that is enabled: Start() runs once per script, the first time the scene is started with
    /// it on. A script that is off is left for <see cref="Update"/>, which starts it on the frame it is switched
    /// on — Unity's rule. A script the editor holds back (<see cref="Script.IsSuspended"/>) does start, because
    /// this is also how a scene is started on its way into Play.
    /// </summary>
    public void Start(PhysicsSystem physicsSystem, AudioSystem audioSystem)
    {
        Logger.Info($"[Scene] Starting scene '{Name}' with {GameObjects.Count} objects.");
        FindMainComponents();
        _physicsSystem = physicsSystem;

        foreach (GameObject gameObject in GameObjects)
        {
            foreach (Script script in gameObject.GetComponents<Script>())
            {
                if (!script.Enabled || script.HasStarted)
                {
                    continue;
                }

                StartScript(gameObject, script, physicsSystem);
            }

            foreach (AudioSource audioSource in gameObject.GetComponents<AudioSource>())
            {
                if (!audioSource.Enabled)
                {
                    continue;
                }

                audioSource.Initialize(audioSystem);
                audioSource.Start();
            }
        }

        if (MainCamera is not null)
        {
            return;
        }

        CreateDefaultCamera();
    }

    /// <summary>
    /// Runs Start() on a script, once: a rigid body gets its physics body first. A throw is logged and holds the
    /// script back (<see cref="Script.IsSuspended"/>), so it is not thrown again every frame.
    /// </summary>
    private static void StartScript(GameObject gameObject, Script script, PhysicsSystem physicsSystem)
    {
        script.HasStarted = true;
        Logger.Info($"[Scene] Initializing script '{script.GetType().Name}' on '{gameObject.Name}'");

        if (script is RigidBody rb)
        {
            try
            {
                rb.Initialize(physicsSystem);
            }
            catch (Exception ex)
            {
                Logger.Error($"[Scene] Failed to initialize RigidBody on '{gameObject.Name}': {ex.Message}");
            }
        }

        try
        {
            script.Start();
        }
        catch (Exception ex)
        {
            Logger.Error($"[Scene] Exception in {script.GetType().Name}.Start() on '{gameObject.Name}': {ex.Message}\n{ex.StackTrace}");
            script.IsSuspended = true;
        }
    }

    public void Update(float deltaTime)
    {
        _debugLogTimer += deltaTime;
        bool shouldLog = _debugLogTimer >= 2.0f;

        if (shouldLog)
        {
            _debugLogTimer = 0;
            // Logger.Info($"[Scene] Heartbeat - Updating {GameObjects.Count} objects.");
        }

        foreach (GameObject gameObject in GameObjects)
        {
            foreach (Script script in gameObject.GetComponents<Script>())
            {
                // Switched off by the author, or held back by the editor or after a throw: either way, no tick.
                if (!script.Enabled || script.IsSuspended)
                {
                    continue;
                }

                // Switched on after the scene was started, or off when it was: Start() runs now, before the first
                // tick. A scene that was never started — the desktop editor's edit mode — starts nothing here.
                if (!script.HasStarted && _physicsSystem is { } physicsSystem)
                {
                    StartScript(gameObject, script, physicsSystem);

                    if (script.IsSuspended)
                    {
                        continue;
                    }
                }

                if (shouldLog)
                {
                    // Logger.Info($"[Scene] Updating {script.GetType().Name} on {gameObject.Name}");
                }

                try
                {
                    script.Update(deltaTime);
                }
                catch (Exception ex)
                {
                    Logger.Error($"[Scene] Exception in {script.GetType().Name}.Update() on '{gameObject.Name}': {ex.Message}");
                    script.IsSuspended = true;
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
            // A component the author switched off is no candidate: a disabled camera is not the main one, a
            // disabled light does not light, and a disabled skybox lets the engine's own background show.
            if (MainCamera is null && gameObject.GetComponent<Camera>() is { Enabled: true } camera)
            {
                MainCamera = camera;
            }

            if (Skybox is null && gameObject.GetComponent<Skybox>() is { Enabled: true } skybox)
            {
                Skybox = skybox;
            }

            Light? light = gameObject.GetComponent<Light>();

            if (light is null || !light.Enabled)
            {
                continue;
            }

            Lights.Add(light);
        }
    }

    private void CreateDefaultCamera()
    {
        Console.WriteLine("Warning: No active camera found in scene. Creating a default one.");
        
        GameObject go = new("Default Camera");
        go.Transform.Position = new(0, 1, 3);
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