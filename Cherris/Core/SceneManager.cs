namespace Cherris;

public class SceneManager
{
    private readonly List<Scene> _openScenes = new();
    private Scene _activeScene;
    public event Action<Scene> OnActiveSceneChanged;


    public Scene ActiveScene => _activeScene;
    public IReadOnlyList<Scene> OpenScenes => _openScenes;
    public PhysicsSystem PhysicsSystem { get; }

    public Camera MainCamera => _activeScene?.MainCamera;
    public Skybox Skybox => _activeScene?.Skybox;
    public IEnumerable<GameObject> GameObjects => _activeScene?.GameObjects ?? Enumerable.Empty<GameObject>();

    public SceneManager()
    {
        PhysicsSystem = new PhysicsSystem();
    }

    public void AddGameObject(GameObject go)
    {
        _activeScene?.AddGameObject(go);
    }

    public void RemoveGameObject(GameObject go)
    {
        if (_activeScene is null) return;

        var rb = go.GetComponent<RigidBody>();
        if (rb?.JitterBody != null)
        {
            PhysicsSystem.RemoveBody(rb.JitterBody);
        }

        _activeScene.RemoveGameObject(go);
    }

    public void OpenScene(Scene scene)
    {
        if (_openScenes.All(s => s.Id != scene.Id))
        {
            _openScenes.Add(scene);
        }
        _activeScene = scene;
        OnActiveSceneChanged?.Invoke(_activeScene);
    }

    public void CloseScene(Scene scene)
    {
        if (scene == null) return;

        int sceneIndex = _openScenes.IndexOf(scene);
        if (sceneIndex == -1) return;

        scene.Dispose();
        _openScenes.RemoveAt(sceneIndex);

        if (_activeScene == scene)
        {
            if (_openScenes.Count > 0)
            {
                // Set the active scene to the one before the closed one, or the first one
                _activeScene = _openScenes[Math.Max(0, sceneIndex - 1)];
            }
            else
            {
                _activeScene = null;
            }
            OnActiveSceneChanged?.Invoke(_activeScene);
        }
    }

    public void SetActiveScene(Scene scene)
    {
        if (scene != null && _openScenes.Contains(scene) && _activeScene != scene)
        {
            _activeScene = scene;
            OnActiveSceneChanged?.Invoke(_activeScene);
        }
    }

    public void SetMainCamera(Camera camera)
    {
        if (_activeScene != null)
        {
            _activeScene.MainCamera = camera;
        }
    }

    public void Start()
    {
        // Start only the active scene when entering play mode.
        _activeScene?.Start(PhysicsSystem);
    }

    public void Update(float deltaTime, bool stepPhysics = true)
    {
        // Update physics first
        if (stepPhysics)
        {
            PhysicsSystem.Update(deltaTime);
        }

        // Then update game logic
        _activeScene?.Update(deltaTime);
    }



    public void Dispose()
    {
        foreach (var scene in _openScenes)
        {
            scene.Dispose();
        }
        _openScenes.Clear();
    }
}