using Overcore.Core;
using Overcore.Core.Logging;
using Overcore.Utils;
using System.Collections;
using System.Reflection;

namespace Overcore.Components;

/// <summary>
/// A component that ticks, Unity's <c>MonoBehaviour</c>: the hooks a scene calls as it runs, plus coroutines,
/// timed calls and the object helpers a script reaches for while it runs.
/// </summary>
/// <remarks>
/// <para>
/// The order a frame runs in is Unity's, and each step is a separate hook:
/// <see cref="FixedUpdate"/> at every physics step (zero or more times a frame, before the step is simulated),
/// then <see cref="Update"/> once per frame, then coroutines, then <see cref="LateUpdate"/>. Physics callbacks —
/// <see cref="OnCollisionEnter(Collision)"/> and friends — arrive after the fixed step that found the contact.
/// </para>
/// <para>
/// A script only runs while it is <see cref="Component.Enabled"/> and its object is active, and never while
/// <see cref="IsSuspended"/> holds it back. A throw from <see cref="Start"/> or <see cref="Update"/> is logged
/// and suspends the script, so one bad frame does not repeat sixty times a second.
/// </para>
/// </remarks>
public abstract class Script : Component
{
    /// <summary>
    /// True while the engine is holding an enabled script back, so it does not tick: the desktop editor sets it
    /// in edit mode, where only a rigid body runs (for its debug drawing), and clears it on Play; and a scene
    /// sets it after a <see cref="Start"/> or <see cref="Update"/> threw, so the error is logged once rather than
    /// every frame.
    /// </summary>
    /// <remarks>
    /// The author's switch is <see cref="Component.Enabled"/>, which the inspector shows and the scene saves.
    /// This one belongs to the editor and to the engine, and is never written to a scene.
    /// </remarks>
    [HideInInspector]
    public bool IsSuspended { get; set; }

    /// <summary>
    /// True once <see cref="Start"/> has run for this script since its scene was started. A scene starts the
    /// scripts that are on at that moment, and starts the others on the frame they are switched on, before their
    /// first <see cref="Update"/> — so Start() runs exactly once per script, however often the toggle moves.
    /// </summary>
    [HideInInspector]
    public bool HasStarted { get; internal set; }

    private readonly List<Coroutine> _coroutines = [];
    private readonly List<InvokeEntry> _invokes = [];

    /// <summary>The object this script is on — Unity's <c>MonoBehaviour.gameObject</c>.</summary>
    public GameObject gameObject => GameObject
        ?? throw new InvalidOperationException("This script is not attached to a GameObject.");

    /// <summary>The object's transform — Unity's <c>MonoBehaviour.transform</c>.</summary>
    public Transform transform => Transform;

    /// <summary>The object's name — Unity's <c>Object.name</c>.</summary>
    public string name
    {
        get => GameObject?.Name ?? string.Empty;
        set
        {
            if (GameObject is { } gameObject)
            {
                gameObject.Name = value;
            }
        }
    }

    /// <summary>Runs once, before the first <see cref="Update"/>, when the scene starts.</summary>
    public virtual void Start() { }

    /// <summary>Runs once per frame, by the frame's scaled delta in seconds.</summary>
    /// <param name="deltaTime">Seconds since the previous frame, scaled by <c>Time.TimeScale</c>.</param>
    public virtual void Update(float deltaTime) { }

    /// <summary>Runs before every physics step, by the physics step in seconds.</summary>
    public virtual void FixedUpdate() { }

    /// <summary>Runs once per frame, after every <see cref="Update"/> and every coroutine has had its turn.</summary>
    public virtual void LateUpdate() { }

    /// <summary>Runs when this object's collider starts touching another's.</summary>
    /// <param name="collision">What was touched, and where.</param>
    public virtual void OnCollisionEnter(Collision collision) { }

    /// <summary>Runs every fixed step the touch lasts.</summary>
    /// <param name="collision">What is touched, and where.</param>
    public virtual void OnCollisionStay(Collision collision) { }

    /// <summary>Runs when the touch ends.</summary>
    /// <param name="collision">What was touched.</param>
    public virtual void OnCollisionExit(Collision collision) { }

    /// <summary>Runs when this object's collider enters another collider marked as a trigger.</summary>
    /// <param name="other">The trigger that was entered.</param>
    public virtual void OnTriggerEnter(Collider other) { }

    /// <summary>Runs every fixed step the overlap lasts.</summary>
    /// <param name="other">The trigger being overlapped.</param>
    public virtual void OnTriggerStay(Collider other) { }

    /// <summary>Runs when the overlap ends.</summary>
    /// <param name="other">The trigger that was left.</param>
    public virtual void OnTriggerExit(Collider other) { }

    // ---- Object helpers, the ones a MonoBehaviour inherits in Unity -------------------------------------------

    /// <summary>Clones an object into the scene, Unity's <c>Instantiate</c>.</summary>
    /// <param name="original">The object to copy, with everything under it.</param>
    public GameObject Instantiate(GameObject original) => Core.GameObject.Instantiate(original);

    /// <summary>Clones an object into the scene under a parent.</summary>
    /// <param name="original">The object to copy.</param>
    /// <param name="parent">The parent the clone is put under.</param>
    public GameObject Instantiate(GameObject original, Transform parent) =>
        Core.GameObject.Instantiate(original, parent);

    /// <summary>Clones an object into the scene at a place and angle.</summary>
    /// <param name="original">The object to copy.</param>
    /// <param name="position">Where the clone goes, in world space.</param>
    /// <param name="rotation">How the clone is turned, in world space.</param>
    public GameObject Instantiate(
        GameObject original,
        System.Numerics.Vector3 position,
        System.Numerics.Quaternion rotation) =>
        Core.GameObject.Instantiate(original, position, rotation);

    /// <summary>Clones an object and hands back a component of the given type on the clone.</summary>
    /// <param name="original">The component whose object is copied.</param>
    /// <returns>The same kind of component, on the copy.</returns>
    public T Instantiate<T>(T original) where T : Component => Core.GameObject.Instantiate(original);

    /// <summary>Destroys an object at the end of the frame, Unity's <c>Destroy</c>.</summary>
    /// <param name="target">The object to destroy.</param>
    public void Destroy(GameObject target) => Core.GameObject.Destroy(target);

    /// <summary>Destroys a component at the end of the frame.</summary>
    /// <param name="target">The component to remove.</param>
    public void Destroy(Component target) => Core.GameObject.Destroy(target);

    /// <summary>Finds a component of a type on this object, Unity's <c>GetComponent</c>.</summary>
    public T? GetComponent<T>() where T : Component => GameObject?.GetComponent<T>();

    /// <summary>Finds a component of a type on this object or under it.</summary>
    /// <param name="includeInactive">True to look inside objects that are switched off.</param>
    public T? GetComponentInChildren<T>(bool includeInactive = false) where T : Component =>
        GameObject?.GetComponentInChildren<T>(includeInactive);

    /// <summary>Finds a component of a type on this object or above it.</summary>
    public T? GetComponentInParent<T>() where T : Component => GameObject?.GetComponentInParent<T>();

    // ---- Coroutines ------------------------------------------------------------------------------------------

    /// <summary>
    /// Starts a routine that runs across frames, Unity's <c>StartCoroutine</c>. The body runs up to its first
    /// yield before this returns.
    /// </summary>
    /// <param name="routine">The routine to run.</param>
    /// <returns>The running coroutine, which can be stopped.</returns>
    public Coroutine StartCoroutine(IEnumerator routine)
    {
        ArgumentNullException.ThrowIfNull(routine);

        Coroutine coroutine = new(routine, this);
        _coroutines.Add(coroutine);

        // Unity runs a routine up to its first yield at the moment it is started, not on the next frame.
        // Time-based waits count from now on the scene's own clock.
        coroutine.Advance(GameObject?.scene?.Clock ?? 0.0f);

        return coroutine;
    }

    /// <summary>Stops a running coroutine.</summary>
    /// <param name="coroutine">The coroutine to stop.</param>
    public void StopCoroutine(Coroutine coroutine)
    {
        if (coroutine is null)
        {
            return;
        }

        coroutine.Stop();
        _coroutines.Remove(coroutine);
    }

    /// <summary>Stops the coroutine started from a routine, if it is still running.</summary>
    /// <param name="routine">The routine whose coroutine should stop.</param>
    public void StopCoroutine(IEnumerator routine)
    {
        if (routine is null)
        {
            return;
        }

        foreach (Coroutine coroutine in _coroutines.ToList())
        {
            if (ReferenceEquals(coroutine.Owner, this) && coroutine.Matches(routine))
            {
                StopCoroutine(coroutine);
            }
        }
    }

    /// <summary>Stops every coroutine this script started.</summary>
    public void StopAllCoroutines()
    {
        foreach (Coroutine coroutine in _coroutines)
        {
            coroutine.Stop();
        }

        _coroutines.Clear();
    }

    // ---- Timed calls -----------------------------------------------------------------------------------------

    /// <summary>
    /// Calls a method of this script once, after a delay, Unity's <c>Invoke</c>. The method takes no arguments.
    /// </summary>
    /// <param name="methodName">Name of the method to call.</param>
    /// <param name="time">Seconds to wait, in scaled time.</param>
    public void Invoke(string methodName, float time) => AddInvoke(methodName, MathF.Max(time, 0.0f), 0.0f);

    /// <summary>Calls a method of this script repeatedly, after a first delay.</summary>
    /// <param name="methodName">Name of the method to call.</param>
    /// <param name="time">Seconds to wait before the first call, in scaled time.</param>
    /// <param name="repeatRate">Seconds between calls.</param>
    public void InvokeRepeating(string methodName, float time, float repeatRate) =>
        AddInvoke(methodName, MathF.Max(time, 0.0f), MathF.Max(repeatRate, 1.0e-3f));

    /// <summary>Cancels every timed call this script has queued.</summary>
    public void CancelInvoke() => _invokes.Clear();

    /// <summary>Cancels the timed calls to one method.</summary>
    /// <param name="methodName">Name of the method whose calls should stop.</param>
    public void CancelInvoke(string methodName) =>
        _invokes.RemoveAll(entry => string.Equals(entry.MethodName, methodName, StringComparison.Ordinal));

    /// <summary>True while a timed call to the given method is queued, or while any is when given no name.</summary>
    /// <param name="methodName">Name of the method, or null for any.</param>
    public bool IsInvoking(string? methodName = null) => methodName is null
        ? _invokes.Count > 0
        : _invokes.Exists(entry => string.Equals(entry.MethodName, methodName, StringComparison.Ordinal));

    // ---- What the scene drives -------------------------------------------------------------------------------

    /// <summary>
    /// Offers the coroutines this phase's turn. Called by the scene after <see cref="Update"/>, inside a fixed
    /// step and at the end of the frame.
    /// </summary>
    /// <param name="phase">Which part of the frame is running.</param>
    /// <param name="time">The scene's scaled clock, in seconds.</param>
    internal void TickCoroutines(CoroutinePhase phase, float time)
    {
        if (_coroutines.Count == 0)
        {
            return;
        }

        foreach (Coroutine coroutine in _coroutines.ToList())
        {
            if (coroutine.IsFinished)
            {
                continue;
            }

            if (coroutine.ShouldStep(phase, time))
            {
                coroutine.Advance(time);
            }
        }

        _coroutines.RemoveAll(coroutine => coroutine.IsFinished);
    }

    /// <summary>Runs any timed calls that have come due.</summary>
    /// <param name="deltaTime">Seconds since the previous frame, in scaled time.</param>
    internal void TickInvokes(float deltaTime)
    {
        if (_invokes.Count == 0)
        {
            return;
        }

        foreach (InvokeEntry entry in _invokes.ToList())
        {
            entry.Remaining -= deltaTime;

            while (entry.Remaining <= 0.0f)
            {
                entry.Call(this);

                if (entry.RepeatRate <= 0.0f)
                {
                    _invokes.Remove(entry);
                    break;
                }

                entry.Remaining += entry.RepeatRate;

                // A call due exactly now is done once, not over and over: the next is a full period away.
                if (entry.Remaining <= 0.0f)
                {
                    entry.Remaining = entry.RepeatRate;
                }
            }
        }
    }

    /// <summary>Forgets the runner state a clone would otherwise share with the original.</summary>
    internal override void OnInstantiated()
    {
        base.OnInstantiated();
        _coroutines.Clear();
        _invokes.Clear();
        IsSuspended = false;
        HasStarted = false;
    }

    /// <summary>True when a coroutine was started from this exact routine object.</summary>
    /// <param name="routine">The routine to compare against.</param>
    internal bool OwnsRoutine(IEnumerator routine) => _coroutines.Exists(coroutine => coroutine.Matches(routine));

    protected void Log(string message)
    {
        Logger.Info($"[{GameObject?.Name ?? "Unassigned"}:{GetType().Name}] {message}");
    }

    protected void LogWarning(string message)
    {
        Logger.Warning($"[{GameObject?.Name ?? "Unassigned"}:{GetType().Name}] {message}");
    }

    protected void LogError(string message)
    {
        Logger.Error($"[{GameObject?.Name ?? "Unassigned"}:{GetType().Name}] {message}");
    }

    private void AddInvoke(string methodName, float time, float repeatRate)
    {
        if (string.IsNullOrWhiteSpace(methodName))
        {
            LogError("Invoke needs a method name.");
            return;
        }

        MethodInfo? method = GetType().GetMethod(
            methodName,
            BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic,
            null,
            Type.EmptyTypes,
            null);

        if (method is null)
        {
            LogError($"Invoke could not find a method called '{methodName}' with no parameters.");
            return;
        }

        _invokes.Add(new InvokeEntry(methodName, method, time, repeatRate));
    }

    /// <summary>One queued call: which method, when it is due, and how often it repeats.</summary>
    private sealed class InvokeEntry
    {
        public string MethodName { get; }
        public float RepeatRate { get; }
        public float Remaining { get; set; }

        private readonly MethodInfo _method;

        public InvokeEntry(string methodName, MethodInfo method, float time, float repeatRate)
        {
            MethodName = methodName;
            _method = method;
            Remaining = time;
            RepeatRate = repeatRate;
        }

        public void Call(Script owner)
        {
            try
            {
                _method.Invoke(owner, null);
            }
            catch (TargetInvocationException exception) when (exception.InnerException is not null)
            {
                Logger.Error(
                    $"[{owner.GameObject?.Name ?? "Unassigned"}:{owner.GetType().Name}] " +
                    $"{MethodName} threw: {exception.InnerException.Message}");
            }
            catch (Exception exception)
            {
                Logger.Error(
                    $"[{owner.GameObject?.Name ?? "Unassigned"}:{owner.GetType().Name}] " +
                    $"{MethodName} could not be called: {exception.Message}");
            }
        }
    }
}
