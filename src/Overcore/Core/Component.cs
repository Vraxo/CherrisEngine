using Overcore.Core.Logging;
using Overcore.Utils;

namespace Overcore.Components;

/// <summary>
/// The base of everything that hangs off a <see cref="GameObject"/>, Unity's <c>Component</c>: an identity, an
/// enable toggle, and the four lifecycle calls every component shares.
/// </summary>
/// <remarks>
/// <para>
/// The lifecycle is Unity's. <see cref="Awake"/> runs once, the first time the object is switched on;
/// <see cref="OnEnable"/> every time the object — or this component — is switched on; <see cref="OnDisable"/>
/// every time it is switched off; <see cref="OnDestroy"/> once, when it goes away for good. A component that is
/// switched on and part of an active object is <em>active</em> (<see cref="IsActive"/>) and only an active
/// component does anything.
/// </para>
/// <para>
/// A throw inside one of these is logged against the object and the component carries on, because a bad script
/// should not take the frame down with it.
/// </para>
/// </remarks>
public abstract class Component
{
    [HideInInspector]
    public GameObject? GameObject { get; internal set; }

    /// <summary>The object's transform, for convenience — Unity's <c>Component.transform</c>.</summary>
    public Transform Transform => GameObject?.Transform
        ?? throw new InvalidOperationException("This component is not attached to a GameObject.");

    /// <summary>
    /// The inspector's header checkbox, Unity's <c>enabled</c>: false switches the component off without taking it
    /// off the object — a renderer stops drawing, a light stops lighting, a script stops ticking — and the state
    /// is saved with the scene.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Hidden from the property list because the inspector draws it in the header, and written and read by the
    /// scene serializer itself for every component, whatever its factory reads (<c>SceneSerializer</c>,
    /// <c>SceneLoader</c>).
    /// </para>
    /// <para>
    /// This is the author's decision. Whether a script runs <em>right now</em> is a second question, answered by
    /// <see cref="Script.IsSuspended"/>: the desktop editor holds scripts back while editing, and the scene holds
    /// one back after it threw, without touching this flag.
    /// </para>
    /// </remarks>
    [HideInInspector]
    public bool Enabled
    {
        get => _enabled;
        set
        {
            if (_enabled == value)
            {
                return;
            }

            _enabled = value;

            if (!HasAwoken)
            {
                return;
            }

            if (value)
            {
                ActivateComponent();
            }
            else
            {
                DeactivateComponent();
            }
        }
    }

    private bool _enabled = true;

    /// <summary>True once <see cref="Awake"/> has run. Never goes back to false.</summary>
    [HideInInspector]
    public bool HasAwoken { get; private set; }

    /// <summary>
    /// True while the component is switched on and part of an object that is active: <see cref="OnEnable"/> has
    /// run and <see cref="OnDisable"/> has not. Unity spells the same question <c>isActiveAndEnabled</c>.
    /// </summary>
    [HideInInspector]
    public bool IsActive { get; private set; }

    /// <summary>True once <see cref="OnDestroy"/> has run: the component is off the object for good.</summary>
    [HideInInspector]
    public bool IsDestroyed { get; private set; }

    /// <summary>Runs once, the first time the object the component is on is switched on.</summary>
    public virtual void Awake() { }

    /// <summary>Runs every time the component becomes active: switched on, or its object was.</summary>
    public virtual void OnEnable() { }

    /// <summary>Runs every time the component stops being active: switched off, or its object was.</summary>
    public virtual void OnDisable() { }

    /// <summary>Runs once, when the component is taken off its object or the object goes away.</summary>
    public virtual void OnDestroy() { }

    /// <summary>
    /// Wakes the component and switches it on if it belongs to an active object. The scene calls this on every
    /// component before any script starts; <see cref="GameObject.AddComponent{T}"/> does it for components added
    /// to an object that is already running.
    /// </summary>
    internal void AwakeComponent()
    {
        if (IsDestroyed)
        {
            return;
        }

        if (!HasAwoken)
        {
            HasAwoken = true;
            Run(Awake);
        }

        if (GameObject?.ActiveInHierarchy == true)
        {
            ActivateComponent();
        }
    }

    /// <summary>Switches the component on if it is allowed to be: it must be enabled and its object active.</summary>
    internal void ActivateComponent()
    {
        if (IsDestroyed || IsActive || !_enabled)
        {
            return;
        }

        if (!HasAwoken)
        {
            AwakeComponent();
            return;
        }

        if (GameObject?.ActiveInHierarchy != true)
        {
            return;
        }

        IsActive = true;
        Run(OnEnable);
    }

    /// <summary>Switches the component off, running <see cref="OnDisable"/> if it was on.</summary>
    internal void DeactivateComponent()
    {
        if (!IsActive)
        {
            return;
        }

        IsActive = false;
        Run(OnDisable);
    }

    /// <summary>Takes the component down for good: <see cref="OnDisable"/>, then <see cref="OnDestroy"/>.</summary>
    internal void DestroyComponent()
    {
        if (IsDestroyed)
        {
            return;
        }

        DeactivateComponent();
        IsDestroyed = true;
        Run(OnDestroy);
    }

    /// <summary>
    /// Puts the runtime state of a cloned component back to how a fresh one looks. A clone copies its fields —
    /// its settings — but not the handles, subscriptions and runner state that belonged to the original.
    /// </summary>
    internal virtual void OnInstantiated()
    {
        HasAwoken = false;
        IsActive = false;
        IsDestroyed = false;
    }

    /// <summary>Runs a lifecycle hook, logging rather than rethrowing whatever it throws.</summary>
    /// <param name="hook">The hook to run.</param>
    private protected void Run(Action hook)
    {
        try
        {
            hook();
        }
        catch (Exception exception)
        {
            Logger.Error(
                $"[{GameObject?.Name ?? "Unassigned"}:{GetType().Name}] {hook.Method.Name} threw: {exception.Message}");
        }
    }
}
