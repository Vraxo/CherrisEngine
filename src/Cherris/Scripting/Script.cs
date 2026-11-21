using Cherris.Core.Logging;

namespace Cherris.Components;

public abstract class Script : Component
{
    public bool Enabled { get; set; } = true;

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