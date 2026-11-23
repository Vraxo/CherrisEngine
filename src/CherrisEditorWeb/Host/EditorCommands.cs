using Cherris.Components;
using Cherris.Core;
using System.Collections.Concurrent;

namespace CherrisEditorWeb.Host;

/// <summary>
/// A change the editor UI asks the engine to make.
/// </summary>
/// <remarks>
/// The UI thread never touches the scene: <see cref="Execute"/> is called by the render thread, which owns
/// it. That is why the web editor can edit at all while its panels render from immutable snapshots.
/// </remarks>
public interface IEditorCommand
{
    /// <summary>One line naming the change, for logs and diagnostics.</summary>
    string Describe { get; }

    /// <summary>Applies the change to the scene the render thread owns.</summary>
    /// <param name="scene">The active scene.</param>
    /// <returns>A line to log, or null when there is nothing worth saying.</returns>
    string? Execute(Scene scene);
}

/// <summary>
/// The UI-to-engine hand-off: any thread enqueues, the render thread drains once per frame.
/// </summary>
/// <remarks>
/// A queue rather than a lock on the scene, so a slow frame cannot block the Blazor dispatcher and a command
/// can never run halfway through a frame. Commands run oldest first, which keeps "add, then tweak" in order.
/// </remarks>
public sealed class EditorCommandQueue
{
    private readonly ConcurrentQueue<IEditorCommand> _pending = new();

    /// <summary>How many commands are waiting to run.</summary>
    public int Count => _pending.Count;

    /// <summary>Adds a command for the render thread to run on its next frame.</summary>
    /// <param name="command">The command.</param>
    public void Enqueue(IEditorCommand command)
    {
        ArgumentNullException.ThrowIfNull(command);

        _pending.Enqueue(command);
    }

    /// <summary>Runs every waiting command, oldest first.</summary>
    /// <param name="execute">Runs one command and returns the line to log, or null.</param>
    /// <param name="log">Receives those lines and any failure, in order.</param>
    /// <returns>How many commands ran.</returns>
    public int Drain(Func<IEditorCommand, string?> execute, Action<string>? log = null)
    {
        ArgumentNullException.ThrowIfNull(execute);

        int ran = 0;

        while (_pending.TryDequeue(out IEditorCommand? command))
        {
            ran++;

            try
            {
                string? line = execute(command);

                if (!String.IsNullOrEmpty(line))
                {
                    log?.Invoke(line);
                }
            }
            catch (Exception exception)
            {
                // One bad command must not take the render thread down with it.
                log?.Invoke($"[editor] {command.Describe} failed: {exception.Message}");
            }
        }

        return ran;
    }
}

/// <summary>
/// Adds a component to an object in the live scene — what the inspector's Add Component menu sends.
/// </summary>
/// <param name="ObjectId">The object to add to.</param>
/// <param name="ComponentName">One of <see cref="ComponentCatalog.BuiltIn"/>'s display names.</param>
/// <remarks>
/// An object holds at most one of each type, the same rule the ImGui inspector applies, so the menu never
/// offers a duplicate and a stale menu cannot make one either.
/// </remarks>
public sealed record AddComponentCommand(Guid ObjectId, string ComponentName) : IEditorCommand
{
    /// <inheritdoc />
    public string Describe => $"add {ComponentName}";

    /// <inheritdoc />
    public string? Execute(Scene scene)
    {
        ArgumentNullException.ThrowIfNull(scene);

        GameObject? target = scene.GameObjects.FirstOrDefault(gameObject => gameObject.Id == ObjectId);

        if (target is null)
        {
            return $"[editor] could not {Describe}: that object is no longer in the scene.";
        }

        if (ComponentCatalog.Find(ComponentName) is not { } entry)
        {
            return $"[editor] could not {Describe}: no component is named '{ComponentName}'.";
        }

        if (target.Components.Any(component => component.GetType() == entry.ComponentType))
        {
            return $"[editor] {target.Name} already has a {entry.DisplayName}.";
        }

        target.AddComponent(entry.Create());

        // A new camera or light changes what the renderer treats as the main one.
        scene.FindMainComponents();
        scene.IsDirty = true;

        return $"[editor] added {entry.DisplayName} to {target.Name}.";
    }
}
