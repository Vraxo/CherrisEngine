using Overcore.Animation;
using Overcore.Components;
using Overcore.Core.Logging;
using Overcore.Core.Physics;
using System.Numerics;

namespace Overcore.Core;

/// <summary>
/// A running world: the objects in it, the camera and lights it is drawn with, and the order its components are
/// ticked in.
/// </summary>
/// <remarks>
/// <para>
/// One frame is Unity's: <see cref="Script.FixedUpdate"/> at every physics step, <see cref="Script.Update"/> once,
/// then coroutines, then <see cref="Script.LateUpdate"/>. Objects that were destroyed during the frame go away at
/// the end of it, so a script can destroy something it is still holding.
/// </para>
/// <para>
/// Starting a scene wakes every component (<see cref="Component.Awake"/>, then <see cref="Component.OnEnable"/>)
/// before any script's <see cref="Script.Start"/>, and a script that is switched on later starts on the frame it is
/// switched on, before its first tick.
/// </para>
/// </remarks>
public class Scene : IDisposable
{
    /// <summary>
    /// The scene new objects join: the one an instantiated clone or
    /// <see cref="GameObject.Find(string)"/> reaches for when it is not told which scene to use. The last scene
    /// opened, or the last one built by hand.
    /// </summary>
    public static Scene? Active { get; internal set; }

    public Guid Id { get; } = Guid.NewGuid();
    public string Name { get; set; }
    public string FilePath { get; set; }
    public List<GameObject> GameObjects { get; } = [];
    public List<Light> Lights { get; } = [];
    public Camera? MainCamera { get; set; }
    public Skybox? Skybox { get; private set; }
    public bool IsDirty { get; set; }

    /// <summary>True once the scene has been started: its components woken and its scripts started.</summary>
    public bool IsStarted => _physicsSystem is not null;

    /// <summary>Scaled seconds the scene has been running, which is the clock coroutines and timed calls count on.</summary>
    public float Clock => _clock;

    // Kept from the last Start(): a script switched on later is started on the frame it is, with the same systems.
    private PhysicsSystem? _physicsSystem;

    private float _debugLogTimer;
    private float _clock;

    public Scene(string filePath, List<GameObject> gameObjects)
    {
        FilePath = filePath;

        Name = string.IsNullOrEmpty(filePath)
            ? "Untitled Scene"
            : Path.GetFileName(filePath);

        GameObjects.AddRange(gameObjects);

        foreach (GameObject gameObject in GameObjects)
        {
            gameObject.scene = this;
        }

        Active = this;
        FindMainComponents();
    }

    public void AddGameObject(GameObject go)
    {
        ArgumentNullException.ThrowIfNull(go);

        go.scene = this;
        GameObjects.Add(go);

        Light? light = go.GetComponent<Light>();

        if (light is not null)
        {
            Lights.Add(light);
        }

        // An object added to a scene that is already running is woken at once, the way Unity treats a clone or a
        // prefab dropped in at run time.
        if (IsStarted && go.ActiveInHierarchy)
        {
            go.AwakeComponents();
        }

        IsDirty = true;
    }

    public void RemoveGameObject(GameObject go)
    {
        foreach (Transform? childTransform in go.Transform.Children.ToList())
        {
            RemoveGameObject(childTransform.GameObject);
        }

        // Whatever is on the object hears about it while the object is still there to be looked at.
        go.DestroyComponents();

        go.Transform.Parent = null!;
        GameObjects.Remove(go);

        if (go.scene == this)
        {
            go.scene = null;
        }

        Light? light = go.GetComponent<Light>();
        
        if (light is not null)
        {
            Lights.Remove(light);
        }

        go.GetComponent<MeshRenderer>()?.Dispose();
        IsDirty = true;
    }

    /// <summary>
    /// Takes an object and its children out of the scene without destroying them, which is how an object that asked
    /// to outlive its scene is carried into the next one.
    /// </summary>
    /// <param name="go">The object to detach.</param>
    internal void DetachGameObject(GameObject go)
    {
        foreach (Transform child in go.Transform.Children.ToList())
        {
            DetachGameObject(child.GameObject);
        }

        GameObjects.Remove(go);

        if (go.GetComponent<Light>() is { } light)
        {
            Lights.Remove(light);
        }

        if (go.scene == this)
        {
            go.scene = null;
        }

        IsDirty = true;
    }

    /// <summary>
    /// Starts the scene: wakes every component — <see cref="Component.Awake"/> for each, then
    /// <see cref="Component.OnEnable"/> — and then runs each script's <see cref="Script.Start"/> once. A script that
    /// is switched off is left for <see cref="Update"/>, which starts it on the frame it is switched on, before its
    /// first tick. A script the editor holds back (<see cref="Script.IsSuspended"/>) does start, because this is
    /// also how a scene is started on its way into Play.
    /// </summary>
    public void Start(PhysicsSystem physicsSystem, AudioSystem audioSystem)
    {
        Logger.Info($"[Scene] Starting scene '{Name}' with {GameObjects.Count} objects.");
        FindMainComponents();
        _physicsSystem = physicsSystem;
        _clock = 0.0f;

        physicsSystem.MakeActive();

        // One handler per scene start, however often the scene is started.
        physicsSystem.FixedStep -= OnFixedStep;
        physicsSystem.FixedStep += OnFixedStep;

        // Unity's order: everything wakes, everything switches on, then scripts start.
        for (int index = 0; index < GameObjects.Count; index++)
        {
            GameObject gameObject = GameObjects[index];

            if (gameObject.ActiveInHierarchy)
            {
                gameObject.AwakeComponents();
            }
        }

        for (int index = 0; index < GameObjects.Count; index++)
        {
            GameObject gameObject = GameObjects[index];

            if (!gameObject.ActiveInHierarchy)
            {
                continue;
            }

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

    /// <summary>
    /// Runs one frame: <see cref="Script.Update"/> on every active script, then the coroutines and timed calls that
    /// are due, then <see cref="Script.LateUpdate"/>, then whatever was destroyed during the frame.
    /// </summary>
    /// <param name="deltaTime">Seconds since the previous frame, already scaled by <c>Time.TimeScale</c>.</param>
    public void Update(float deltaTime)
    {
        _clock += deltaTime;

        _debugLogTimer += deltaTime;

        if (_debugLogTimer >= 2.0f)
        {
            _debugLogTimer = 0;
            // Logger.Info($"[Scene] Heartbeat - Updating {GameObjects.Count} objects.");
        }

        int count = GameObjects.Count;

        for (int index = 0; index < count && index < GameObjects.Count; index++)
        {
            GameObject gameObject = GameObjects[index];

            if (!gameObject.ActiveInHierarchy)
            {
                continue;
            }

            foreach (Script script in gameObject.GetComponents<Script>().ToList())
            {
                TickUpdate(gameObject, script, deltaTime);
            }
        }

        AdvanceCoroutines(CoroutinePhase.Update, deltaTime);
        AdvanceNavigation(deltaTime);
        AdvanceAnimation(deltaTime);
        AdvanceParticles(deltaTime);
        AdvanceTerrains();
        AdvanceLateUpdate();
        AdvanceCoroutines(CoroutinePhase.EndOfFrame, 0.0f);

        PendingDestruction.Flush(deltaTime);
    }

    private void AdvanceNavigation(float deltaTime)
    {
        // Only advance NavMesh obstacles & agents when the scene has been started (play mode / runtime).
        if (_physicsSystem is null || deltaTime <= 0f)
        {
            return;
        }

        List<NavMeshSurface> surfaces = [];
        List<NavMeshObstacle> obstacles = [];
        List<NavMeshAgent> agents = [];

        int count = GameObjects.Count;
        for (int index = 0; index < count && index < GameObjects.Count; index++)
        {
            GameObject go = GameObjects[index];
            if (!go.ActiveInHierarchy)
            {
                continue;
            }

            foreach (NavMeshSurface surface in go.GetComponents<NavMeshSurface>())
            {
                if (!surface.IsDestroyed && surface.Enabled)
                {
                    surfaces.Add(surface);
                }
            }

            foreach (NavMeshObstacle obstacle in go.GetComponents<NavMeshObstacle>())
            {
                if (!obstacle.IsDestroyed)
                {
                    obstacles.Add(obstacle);
                }
            }

            foreach (NavMeshAgent agent in go.GetComponents<NavMeshAgent>())
            {
                if (!agent.IsDestroyed && agent.Enabled)
                {
                    agents.Add(agent);
                }
            }
        }

        // If any carving obstacle moved beyond its threshold, rebuild active surfaces so agents replan cleanly.
        bool anyCarveDirty = false;
        foreach (NavMeshObstacle obstacle in obstacles)
        {
            if (obstacle.PollCarveDirty(deltaTime))
            {
                anyCarveDirty = true;
            }
        }

        if (anyCarveDirty)
        {
            foreach (NavMeshSurface surface in surfaces)
            {
                try
                {
                    surface.BuildNavMesh(GameObjects);
                }
                catch (Exception ex)
                {
                    Logger.Error($"[Scene] Exception in NavMeshSurface.BuildNavMesh() on '{surface.GameObject?.Name}': {ex.Message}");
                }
            }
        }

        foreach (NavMeshAgent agent in agents)
        {
            try
            {
                agent.Step(deltaTime, agents, obstacles);
            }
            catch (Exception ex)
            {
                Logger.Error($"[Scene] Exception in NavMeshAgent.Step() on '{agent.GameObject?.Name}': {ex.Message}");
            }
        }
    }

    private void AdvanceParticles(float deltaTime)
    {
        if (deltaTime <= 0f)
        {
            return;
        }

        int count = GameObjects.Count;
        for (int index = 0; index < count && index < GameObjects.Count; index++)
        {
            GameObject go = GameObjects[index];
            if (!go.ActiveInHierarchy)
            {
                continue;
            }

            foreach (ParticleSystem ps in go.GetComponents<ParticleSystem>().ToList())
            {
                if (ps.IsDestroyed || !ps.Enabled)
                {
                    continue;
                }

                try
                {
                    ps.Step(deltaTime);
                }
                catch (Exception ex)
                {
                    Logger.Error($"[Scene] Exception in ParticleSystem.Step() on '{go.Name}': {ex.Message}");
                }
            }
        }
    }

    /// <summary>
    /// Rebuilds the mesh of any terrain whose data was edited straight — a script raising ground, a brush
    /// stroke that went to the data — so the drawn ground follows the data within a frame.
    /// </summary>
    private void AdvanceTerrains()
    {
        int count = GameObjects.Count;
        for (int index = 0; index < count && index < GameObjects.Count; index++)
        {
            GameObject go = GameObjects[index];
            if (!go.ActiveInHierarchy)
            {
                continue;
            }

            foreach (Terrain terrain in go.GetComponents<Terrain>().ToList())
            {
                if (terrain.IsDestroyed || !terrain.Enabled)
                {
                    continue;
                }

                try
                {
                    terrain.RefreshIfDirty();
                }
                catch (Exception ex)
                {
                    Logger.Error($"[Scene] Exception in Terrain.RefreshIfDirty() on '{go.Name}': {ex.Message}");
                }
            }
        }
    }

    private void TickUpdate(GameObject gameObject, Script script, float deltaTime)
    {
        if (script.IsDestroyed || !script.Enabled || script.IsSuspended)
        {
            return;
        }

        // Switched on after the scene was started, or off when it was: Start() runs now, before the first tick. A
        // scene that was never started — the desktop editor's edit mode — starts nothing here.
        if (!script.HasStarted && _physicsSystem is { } physicsSystem)
        {
            if (!script.IsActive)
            {
                script.AwakeComponent();
            }

            if (!script.IsActive)
            {
                return;
            }

            StartScript(gameObject, script, physicsSystem);

            if (script.IsSuspended || !script.IsActive)
            {
                return;
            }
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

    private void AdvanceCoroutines(CoroutinePhase phase, float deltaTime)
    {
        int count = GameObjects.Count;

        for (int index = 0; index < count && index < GameObjects.Count; index++)
        {
            GameObject gameObject = GameObjects[index];

            if (!gameObject.ActiveInHierarchy)
            {
                continue;
            }

            foreach (Script script in gameObject.GetComponents<Script>().ToList())
            {
                if (script.IsDestroyed || !script.IsActive || script.IsSuspended)
                {
                    continue;
                }

                if (phase == CoroutinePhase.Update && deltaTime > 0.0f)
                {
                    script.TickInvokes(deltaTime);
                }

                try
                {
                    script.TickCoroutines(phase, _clock);
                }
                catch (Exception ex)
                {
                    Logger.Error(
                        $"[Scene] Exception in a {script.GetType().Name} coroutine on '{gameObject.Name}': {ex.Message}");
                    script.IsSuspended = true;
                }
            }
        }
    }

    /// <summary>
    /// Advances every <see cref="AnimationPlayer"/> and writes its pose, between <c>Update</c> and <c>LateUpdate</c>.
    /// </summary>
    /// <remarks>
    /// That placement is Unity's and it matters: a script that moves a bone in <c>Update</c> is overridden by the clip
    /// that drives it, and a script that wants the final pose reads it in <c>LateUpdate</c>. An <see cref="AnimationPlayer"/>
    /// whose clock is paused still applies its pose, so a scrubber needs no playback.
    /// </remarks>
    /// <param name="deltaTime">Seconds since the previous frame, in scaled time.</param>
    private void AdvanceAnimation(float deltaTime)
    {
        int count = GameObjects.Count;

        for (int index = 0; index < count && index < GameObjects.Count; index++)
        {
            GameObject gameObject = GameObjects[index];

            if (!gameObject.ActiveInHierarchy)
            {
                continue;
            }

            foreach (AnimationPlayer animation in gameObject.GetComponents<AnimationPlayer>().ToList())
            {
                if (animation.IsDestroyed || !animation.IsActive)
                {
                    continue;
                }

                try
                {
                    animation.Advance(animation.UseUnscaledTime
                        ? Time.UnscaledDeltaTime
                        : deltaTime);
                }
                catch (Exception ex)
                {
                    Logger.Error(
                        $"[Scene] Exception in AnimationPlayer.Advance() on '{gameObject.Name}': {ex.Message}");
                }
            }

            // An Animator that asked for the physics phase is stepped by OnFixedStep instead, so it is skipped here
            // rather than advanced twice in one frame.
            foreach (Animator animator in gameObject.GetComponents<Animator>().ToList())
            {
                if (animator.IsDestroyed || !animator.IsActive ||
                    animator.UpdateMode == AnimatorUpdateMode.AnimatePhysics)
                {
                    continue;
                }

                try
                {
                    animator.Advance(animator.UpdateMode == AnimatorUpdateMode.UnscaledTime
                        ? Time.UnscaledDeltaTime
                        : deltaTime);
                }
                catch (Exception ex)
                {
                    Logger.Error(
                        $"[Scene] Exception in Animator.Advance() on '{gameObject.Name}': {ex.Message}");
                }
            }
        }
    }

    /// <summary>
    /// Advances the animators that asked for the physics phase, once per fixed step and after the step's physics.
    /// </summary>
    /// <remarks>
    /// This is what a pose that drives colliders needs: an AnimationMode of <c>AnimatePhysics</c> means the pose is
    /// computed on the fixed clock, so a hitbox moved by a clip lands on the same schedule the solver runs on.
    /// </remarks>
    /// <param name="step">The step's length in seconds.</param>
    private void AdvancePhysicsAnimation(float step)
    {
        int count = GameObjects.Count;

        for (int index = 0; index < count && index < GameObjects.Count; index++)
        {
            GameObject gameObject = GameObjects[index];

            if (!gameObject.ActiveInHierarchy)
            {
                continue;
            }

            foreach (Animator animator in gameObject.GetComponents<Animator>().ToList())
            {
                if (animator.IsDestroyed || !animator.IsActive ||
                    animator.UpdateMode != AnimatorUpdateMode.AnimatePhysics)
                {
                    continue;
                }

                try
                {
                    animator.Advance(step);
                }
                catch (Exception ex)
                {
                    Logger.Error(
                        $"[Scene] Exception in Animator.Advance() on '{gameObject.Name}': {ex.Message}");
                }
            }
        }
    }

    private void AdvanceLateUpdate()
    {
        int count = GameObjects.Count;

        for (int index = 0; index < count && index < GameObjects.Count; index++)
        {
            GameObject gameObject = GameObjects[index];

            if (!gameObject.ActiveInHierarchy)
            {
                continue;
            }

            foreach (Script script in gameObject.GetComponents<Script>().ToList())
            {
                if (script.IsDestroyed || !script.IsActive || script.IsSuspended)
                {
                    continue;
                }

                try
                {
                    script.LateUpdate();
                }
                catch (Exception ex)
                {
                    Logger.Error($"[Scene] Exception in {script.GetType().Name}.LateUpdate() on '{gameObject.Name}': {ex.Message}");
                    script.IsSuspended = true;
                }
            }
        }
    }

    /// <summary>
    /// Runs <see cref="Script.FixedUpdate"/> on every active script, and the coroutines waiting for a step. The
    /// physics world raises this before each step it simulates.
    /// </summary>
    /// <param name="step">The step's length in seconds.</param>
    private void OnFixedStep(float step)
    {
        int count = GameObjects.Count;

        for (int index = 0; index < count && index < GameObjects.Count; index++)
        {
            GameObject gameObject = GameObjects[index];

            if (!gameObject.ActiveInHierarchy)
            {
                continue;
            }

            foreach (Script script in gameObject.GetComponents<Script>().ToList())
            {
                if (script.IsDestroyed || !script.IsActive || script.IsSuspended)
                {
                    continue;
                }

                try
                {
                    script.FixedUpdate();
                }
                catch (Exception ex)
                {
                    Logger.Error($"[Scene] Exception in {script.GetType().Name}.FixedUpdate() on '{gameObject.Name}': {ex.Message}");
                    script.IsSuspended = true;
                }

                try
                {
                    script.TickCoroutines(CoroutinePhase.Fixed, _clock);
                }
                catch (Exception ex)
                {
                    Logger.Error($"[Scene] Exception in a {script.GetType().Name} coroutine on '{gameObject.Name}': {ex.Message}");
                    script.IsSuspended = true;
                }
            }
        }

        AdvancePhysicsAnimation(step);

        _ = step;
    }

    public void FindMainComponents()
    {
        MainCamera = null;
        Skybox = null;
        Lights.Clear();

        foreach (GameObject gameObject in GameObjects)
        {
            // An object that is switched off is no candidate at all, and neither is a component the author
            // switched off: a disabled camera is not the main one, a disabled light does not light, and a
            // disabled skybox lets the engine's own background show.
            if (!gameObject.ActiveInHierarchy)
            {
                continue;
            }

            MainCamera ??= gameObject.GetComponents<Camera>().FirstOrDefault(camera => camera.Enabled);
            Skybox ??= gameObject.GetComponents<Skybox>().FirstOrDefault(skybox => skybox.Enabled);
            Lights.AddRange(gameObject.GetComponents<Light>().Where(light => light.Enabled));
        }
    }

    private void CreateDefaultCamera()
    {
        Console.WriteLine("Warning: No active camera found in scene. Creating a default one.");
        
        GameObject go = new("Default Camera");
        go.Transform.Position = new(0, 1, 3);
        MainCamera = go.AddComponent(new Camera());
        AddGameObject(go);
    }

    public void Dispose()
    {
        if (_physicsSystem is { } physicsSystem)
        {
            physicsSystem.FixedStep -= OnFixedStep;
        }

        foreach (GameObject gameObject in GameObjects.ToList())
        {
            gameObject.DestroyComponents();
            gameObject.GetComponent<MeshRenderer>()?.Dispose();

            // Everything still in the scene goes with it: the objects are destroyed, not merely forgotten.
            gameObject.IsDestroyed = true;
            gameObject.scene = null;
        }

        GameObjects.Clear();
        Lights.Clear();
        MainCamera = null;
        Skybox = null;
        _physicsSystem = null;

        if (ReferenceEquals(Active, this))
        {
            Active = null;
        }
    }
}
