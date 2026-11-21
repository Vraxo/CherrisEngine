using Cherris.Components;
using Cherris.Core.Physics;

namespace Cherris.Core;

public class SceneManager
{
    private readonly List<Scene> _openScenes = [];

    public event Action<Scene> OnActiveSceneChanged;


    public Scene? ActiveScene { get; private set; }
    public IReadOnlyList<Scene> OpenScenes => _openScenes;
    public PhysicsSystem PhysicsSystem { get; }
    public AudioSystem AudioSystem { get; }

    public Camera? MainCamera => ActiveScene?.MainCamera;
    public Skybox? Skybox => ActiveScene?.Skybox;
    public IEnumerable<GameObject> GameObjects => ActiveScene?.GameObjects ?? Enumerable.Empty<GameObject>();
    public IEnumerable<Light> Lights => ActiveScene?.Lights ?? Enumerable.Empty<Light>();

    public SceneManager()
    {
        PhysicsSystem = new PhysicsSystem();
        AudioSystem = new AudioSystem();
        AudioSystem.Initialize();
    }

    public void AddGameObject(GameObject go)
    {
        ActiveScene?.AddGameObject(go);
    }

    public void RemoveGameObject(GameObject go)
    {
        if (ActiveScene is null)
        {
            return;
        }

        var rb = go.GetComponent<RigidBody>();

        // BEPUphysics v2: Use BepuBodyHandle/BepuStaticHandle instead of JitterBody
        if (rb?.BepuBodyHandle.HasValue == true)
        {
            PhysicsSystem.Simulation.Bodies.Remove(rb.BepuBodyHandle.Value);
            PhysicsSystem.UnregisterBody(rb);
            rb.BepuBodyHandle = null;
        }

        if (rb?.BepuStaticHandle.HasValue == true)
        {
            PhysicsSystem.Simulation.Statics.Remove(rb.BepuStaticHandle.Value);
            PhysicsSystem.UnregisterBody(rb);
            rb.BepuStaticHandle = null;
        }

        ActiveScene.RemoveGameObject(go);
    }

    public void OpenScene(Scene scene)
    {
        if (_openScenes.All(s => s.Id != scene.Id))
        {
            _openScenes.Add(scene);
        }
        ActiveScene = scene;
        OnActiveSceneChanged?.Invoke(ActiveScene);
    }

    public void CloseScene(Scene scene)
    {
        if (scene is null)
        {
            return;
        }

        int sceneIndex = _openScenes.IndexOf(scene);
        if (sceneIndex == -1)
        {
            return;
        }

        scene.Dispose();
        _openScenes.RemoveAt(sceneIndex);

        if (ActiveScene == scene)
        {
            if (_openScenes.Count > 0)
            {
                // Set the active scene to the one before the closed one, or the first one
                ActiveScene = _openScenes[Math.Max(0, sceneIndex - 1)];
            }
            else
            {
                ActiveScene = null;
            }
            OnActiveSceneChanged?.Invoke(ActiveScene);
        }
    }

    public void SetActiveScene(Scene scene)
    {
        if (scene is not null && _openScenes.Contains(scene) && ActiveScene != scene)
        {
            ActiveScene = scene;
            OnActiveSceneChanged?.Invoke(ActiveScene);
        }
    }

    public void SetMainCamera(Camera camera)
    {
        ActiveScene?.MainCamera = camera;
    }

    public void Start()
    {
        // Start only the active scene when entering play mode.
        ActiveScene?.Start(PhysicsSystem, AudioSystem);
    }

    public void Update(float deltaTime, bool stepPhysics = true)
    {
        // Update physics first
        if (stepPhysics)
        {
            PhysicsSystem.Update(deltaTime);
        }

        // Update audio system
        AudioSystem.Update(ActiveScene);

        // Then update game logic
        ActiveScene?.Update(deltaTime);
    }

    public void Dispose()
    {
        foreach (var scene in _openScenes)
        {
            scene.Dispose();
        }
        _openScenes.Clear();
        AudioSystem.Dispose();
    }
}