using Overcore.Components;
using Overcore.Core;
using Overcore.Scripting;
using Overcore.Serialization;
using Overcore.Utils;
using System.Collections.Concurrent;
using System.Numerics;

namespace OvercoreEditorSession.Host;

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
