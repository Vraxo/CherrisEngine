using Overcore.Components;
using Overcore.Core.Logging;
using Overcore.Core.Physics;
using Overcore.Serialization;

namespace Overcore.Core;

/// <summary>
/// The scenes that are open, which of them is running, and the world they run in — Unity's <c>SceneManager</c> plus
/// the frame loop that drives it.
/// </summary>
/// <remarks>
/// <para>
/// A frame is one call to <see cref="Update(float, bool)"/>: the clock is advanced, physics steps, audio follows the
/// active scene, and then the scene ticks. Time scaling happens here, once, so everything downstream moves at the
/// same speed.
/// </para>
/// <para>
/// <see cref="LoadScene(string)"/> is Unity's scene change: the scene that was running is closed, its objects
/// destroyed, and the new file loaded and started. Whatever asked for <see cref="DontDestroyOnLoad(GameObject)"/>
/// is carried across rather than destroyed.
/// </para>
/// </remarks>
public class SceneManager
{
    private readonly List<Scene> _openScenes = [];
    private readonly List<GameObject> _persistentObjects = [];

    public event Action<Scene> OnActiveSceneChanged;

    public Scene? ActiveScene { get; private set; }
    public IReadOnlyList<Scene> OpenScenes => _openScenes;

    /// <summary>Objects asked to outlive the scene they are in, Unity's "don't destroy on load" set.</summary>
    public IReadOnlyList<GameObject> PersistentObjects => _persistentObjects;

    public PhysicsSystem PhysicsSystem { get; }
    public AudioSystem AudioSystem { get; }

    /// <summary>
    /// How a scene file is read. The engine sets this when it starts; without it
    /// <see cref="LoadScene(string)"/> has nothing to read the file with.
    /// </summary>
    public SceneLoader? SceneLoader { get; set; }

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
        ArgumentNullException.ThrowIfNull(scene);

        if (_openScenes.All(s => s.Id != scene.Id))
        {
            _openScenes.Add(scene);
        }

        ActiveScene = scene;
        Scene.Active = scene;
        OnActiveSceneChanged?.Invoke(ActiveScene);
    }

    public void CloseScene(Scene scene)
    {
        UnloadScene(scene);
    }

    /// <summary>
    /// Closes a scene and destroys everything in it — Unity's <c>UnloadScene</c>. The active scene becomes the one
    /// opened before it, or nothing when it was the last.
    /// </summary>
    /// <param name="scene">The scene to unload.</param>
    /// <returns>True when the scene was open and is now closed.</returns>
    public bool UnloadScene(Scene scene)
    {
        if (scene is null)
        {
            return false;
        }

        int sceneIndex = _openScenes.IndexOf(scene);

        if (sceneIndex == -1)
        {
            return false;
        }

        scene.Dispose();
        _openScenes.RemoveAt(sceneIndex);

        if (ActiveScene == scene)
        {
            ActiveScene = _openScenes.Count > 0
                ? _openScenes[Math.Max(0, sceneIndex - 1)]
                : null;

            Scene.Active = ActiveScene;
            OnActiveSceneChanged?.Invoke(ActiveScene!);
        }

        return true;
    }

    /// <summary>Closes the scene with the given name or file path — Unity's <c>UnloadScene</c> by name.</summary>
    /// <param name="nameOrPath">The scene's name, its file name, or its full path.</param>
    /// <returns>True when a scene matched and is now closed.</returns>
    public bool UnloadScene(string nameOrPath)
    {
        if (string.IsNullOrWhiteSpace(nameOrPath))
        {
            return false;
        }

        Scene? scene = _openScenes.FirstOrDefault(open =>
            string.Equals(open.Name, nameOrPath, StringComparison.OrdinalIgnoreCase)
            || string.Equals(open.FilePath, nameOrPath, StringComparison.OrdinalIgnoreCase)
            || string.Equals(Path.GetFileName(open.FilePath), nameOrPath, StringComparison.OrdinalIgnoreCase));

        return scene is not null && UnloadScene(scene);
    }

    /// <summary>
    /// Loads a scene file and runs it, closing the scene that was running first — Unity's <c>LoadScene</c>.
    /// </summary>
    /// <param name="scenePath">The scene file to load.</param>
    /// <returns>The new scene, or null when there is no loader or the file could not be read.</returns>
    public Scene? LoadScene(string scenePath)
    {
        if (string.IsNullOrWhiteSpace(scenePath))
        {
            throw new ArgumentException("A scene path is needed to load a scene.", nameof(scenePath));
        }

        if (SceneLoader is null)
        {
            Logger.Error("[SceneManager] No scene loader is set, so no scene can be loaded.");
            return null;
        }

        List<GameObject> loaded = SceneLoader.LoadScene(scenePath);

        if (loaded.Count == 0)
        {
            // The loader has already said why; nothing is loaded, so nothing is closed either.
            return null;
        }

        // Whatever was told to outlive the scene is taken out of it before it goes, and put into the new one after.
        Scene? previous = ActiveScene;

        if (previous is not null)
        {
            foreach (GameObject keep in _persistentObjects.ToList())
            {
                previous.DetachGameObject(keep);
                keep.Transform.Parent = null!;
            }

            UnloadScene(previous);
        }

        Scene scene = new(scenePath, loaded);
        OpenScene(scene);

        foreach (GameObject keep in _persistentObjects)
        {
            keep.AssignScene(scene);
            scene.AddGameObject(keep);
        }

        Start();
        return scene;
    }

    /// <summary>
    /// Keeps an object when its scene is unloaded, Unity's <c>DontDestroyOnLoad</c>: it is carried into whichever
    /// scene is loaded next instead of going away with the one it was in.
    /// </summary>
    /// <param name="target">The object to keep.</param>
    public void DontDestroyOnLoad(GameObject target)
    {
        ArgumentNullException.ThrowIfNull(target);

        if (!_persistentObjects.Contains(target))
        {
            _persistentObjects.Add(target);
        }
    }

    /// <summary>Forgets that an object was meant to outlive its scene.</summary>
    /// <param name="target">The object to stop carrying across.</param>
    public bool UndoDontDestroyOnLoad(GameObject target) => _persistentObjects.Remove(target);

    public void SetActiveScene(Scene scene)
    {
        if (scene is not null && _openScenes.Contains(scene) && ActiveScene != scene)
        {
            ActiveScene = scene;
            Scene.Active = scene;
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

    /// <summary>
    /// Runs one frame. The clock is advanced first and everything else moves by the scaled delta it hands back, so
    /// <c>Time.TimeScale</c> slows the physics, the scripts and the coroutines together.
    /// </summary>
    /// <param name="deltaTime">Seconds since the previous frame, as the host measured them.</param>
    /// <param name="stepPhysics">False to hold physics still — the editor does this while it is not playing.</param>
    public void Update(float deltaTime, bool stepPhysics = true)
    {
        float scaledDeltaTime = Time.Advance(deltaTime);

        if (stepPhysics)
        {
            PhysicsSystem.Update(scaledDeltaTime);
        }

        AudioSystem.Update(ActiveScene);

        ActiveScene?.Update(scaledDeltaTime);
    }

    public void Dispose()
    {
        foreach (var scene in _openScenes)
        {
            scene.Dispose();
        }

        _openScenes.Clear();
        _persistentObjects.Clear();
        ActiveScene = null;
        PendingDestruction.Clear();
        AudioSystem.Dispose();
        PhysicsSystem.Dispose();
    }
}
