using Cherris.Core.Logging;
using Cherris.Utils;

namespace Cherris.Components;

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

    public virtual void Start() { }
    public virtual void Update(float deltaTime) { }

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
}