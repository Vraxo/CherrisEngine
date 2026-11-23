using Cherris.Components;
using Cherris.Core;
using Cherris.Scripting;
using System.Collections.Concurrent;
using System.Numerics;

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
    /// <param name="context">The scene, and the history an undoable command records itself in.</param>
    /// <returns>A line to log, or null when there is nothing worth saying.</returns>
    string? Execute(EditorCommandContext context);
}

/// <summary>A command that can be taken back.</summary>
/// <remarks>
/// <see cref="Execute"/> records the command in <see cref="EditorCommandContext.History"/> when it actually
/// changes something; <see cref="Undo"/> and <see cref="Redo"/> re-apply the values the command captured and
/// never record, so the stacks keep their meaning.
/// </remarks>
public interface IUndoableCommand : IEditorCommand
{
    /// <summary>Puts back what <see cref="IEditorCommand.Execute"/> replaced.</summary>
    /// <param name="context">The scene and history.</param>
    string? Undo(EditorCommandContext context);

    /// <summary>Re-applies the change after an <see cref="Undo"/>.</summary>
    /// <param name="context">The scene and history.</param>
    string? Redo(EditorCommandContext context);
}

/// <summary>What a command runs against: the scene, the history it may record itself in, and the project.</summary>
/// <param name="scene">The active scene, owned by the render thread.</param>
/// <param name="history">The undo stack for this session.</param>
/// <param name="projectRoot">
/// Absolute folder of the open project, or null in the built-in demo scene. A command that writes a file —
/// a new script — needs it; nothing else does.
/// </param>
public sealed class EditorCommandContext(Scene scene, EditorHistory history, string? projectRoot = null)
{
    /// <summary>The active scene.</summary>
    public Scene Scene { get; } = scene ?? throw new ArgumentNullException(nameof(scene));

    /// <summary>The session's history.</summary>
    public EditorHistory History { get; } = history ?? throw new ArgumentNullException(nameof(history));

    /// <summary>The open project's folder, or null when there is no project to write to.</summary>
    public string? ProjectRoot { get; } = projectRoot;
}

/// <summary>
/// The session's undo and redo stacks.
/// </summary>
/// <remarks>
/// Written on the render thread only, but read by the UI thread to enable Undo and Redo, so the counts are
/// guarded. A command that is not <see cref="IUndoableCommand"/> is simply never recorded.
/// </remarks>
public sealed class EditorHistory
{
    private const int MaximumDepth = 200;

    private readonly object _gate = new();
    private readonly LinkedList<IUndoableCommand> _undo = new();
    private readonly LinkedList<IUndoableCommand> _redo = new();

    /// <summary>True when there is something to take back.</summary>
    public bool CanUndo
    {
        get
        {
            lock (_gate)
            {
                return _undo.Count > 0;
            }
        }
    }

    /// <summary>True when something taken back can be re-applied.</summary>
    public bool CanRedo
    {
        get
        {
            lock (_gate)
            {
                return _redo.Count > 0;
            }
        }
    }

    /// <summary>What Undo would take back, for the menu's label, or null.</summary>
    public string? UndoLabel
    {
        get
        {
            lock (_gate)
            {
                return _undo.Last?.Value.Describe;
            }
        }
    }

    /// <summary>What Redo would re-apply, for the menu's label, or null.</summary>
    public string? RedoLabel
    {
        get
        {
            lock (_gate)
            {
                return _redo.Last?.Value.Describe;
            }
        }
    }

    /// <summary>Records a change that has just been made; a new change ends the redo branch.</summary>
    /// <param name="command">The command that made it.</param>
    public void Record(IUndoableCommand command)
    {
        ArgumentNullException.ThrowIfNull(command);

        lock (_gate)
        {
            _undo.AddLast(command);
            _redo.Clear();

            while (_undo.Count > MaximumDepth)
            {
                _undo.RemoveFirst();
            }
        }
    }

    /// <summary>Takes the newest change off the undo stack and puts it on the redo stack.</summary>
    public IUndoableCommand? TakeUndo()
    {
        lock (_gate)
        {
            if (_undo.Last is not { } last)
            {
                return null;
            }

            _undo.RemoveLast();
            _redo.AddLast(last);

            return last.Value;
        }
    }

    /// <summary>Takes the newest undone change back off the redo stack.</summary>
    public IUndoableCommand? TakeRedo()
    {
        lock (_gate)
        {
            if (_redo.Last is not { } last)
            {
                return null;
            }

            _redo.RemoveLast();
            _undo.AddLast(last);

            return last.Value;
        }
    }
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
/// The queue and the history together: what a host owns so its engine can apply edits and take them back.
/// </summary>
/// <remarks>
/// Both halves are per session, because a history that outlived the scene it describes would undo into the
/// wrong one.
/// </remarks>
public sealed class EditorCommandDispatcher(string? projectRoot = null)
{
    private readonly EditorCommandQueue _queue = new();

    /// <summary>The open project's folder, or null in the built-in demo scene.</summary>
    public string? ProjectRoot { get; } = projectRoot;

    /// <summary>How many edits are waiting for the render thread.</summary>
    public int Pending => _queue.Count;

    /// <summary>The session's undo and redo stacks.</summary>
    public EditorHistory History { get; } = new();

    /// <summary>Queues an edit. Safe from any thread.</summary>
    /// <param name="command">The command.</param>
    public void Enqueue(IEditorCommand command) => _queue.Enqueue(command);

    /// <summary>Runs the waiting edits against the scene, oldest first. Call from the thread that owns it.</summary>
    /// <param name="scene">The active scene.</param>
    /// <param name="log">Receives the lines the commands return, and any failure.</param>
    public int Drain(Scene scene, Action<string>? log = null)
    {
        ArgumentNullException.ThrowIfNull(scene);

        EditorCommandContext context = new(scene, History, ProjectRoot);

        return _queue.Drain(command => command.Execute(context), log);
    }
}

/// <summary>
/// Adds a component to an object in the live scene — what the inspector's Add Component menu sends.
/// </summary>
/// <param name="ObjectId">The object to add to.</param>
/// <param name="ComponentName">One of <see cref="ComponentCatalog.BuiltIn"/>'s display names.</param>
/// <remarks>
/// An object holds at most one of each type, the same rule the ImGui inspector applies, so the menu never
/// offers a duplicate and a stale menu cannot make one either. Undo removes the component again.
/// </remarks>
public sealed class AddComponentCommand(Guid objectId, string componentName) : IUndoableCommand
{
    private Component? _added;

    /// <inheritdoc />
    public string Describe => $"add {componentName}";

    /// <inheritdoc />
    public string? Execute(EditorCommandContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        if (FindTarget(context.Scene) is not { } target)
        {
            return $"[editor] could not {Describe}: that object is no longer in the scene.";
        }

        if (ComponentCatalog.Find(componentName) is not { } entry)
        {
            return $"[editor] could not {Describe}: no component is named '{componentName}'.";
        }

        if (target.Components.Any(component => component.GetType() == entry.ComponentType))
        {
            return $"[editor] {target.Name} already has a {entry.DisplayName}.";
        }

        _added = entry.Create();
        target.AddComponent(_added);

        // A new camera or light changes what the renderer treats as the main one.
        context.Scene.FindMainComponents();
        context.Scene.IsDirty = true;
        context.History.Record(this);

        return $"[editor] added {entry.DisplayName} to {target.Name}.";
    }

    /// <inheritdoc />
    public string? Undo(EditorCommandContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        if (_added is null || FindTarget(context.Scene) is not { } target)
        {
            return null;
        }

        target.RemoveComponent(_added);
        context.Scene.FindMainComponents();
        context.Scene.IsDirty = true;

        return $"[editor] removed {componentName} from {target.Name}.";
    }

    /// <inheritdoc />
    public string? Redo(EditorCommandContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        if (_added is null || FindTarget(context.Scene) is not { } target)
        {
            return null;
        }

        target.AddComponent(_added);
        context.Scene.FindMainComponents();
        context.Scene.IsDirty = true;

        return $"[editor] added {componentName} back to {target.Name}.";
    }

    private GameObject? FindTarget(Scene scene) =>
        scene.GameObjects.FirstOrDefault(gameObject => gameObject.Id == objectId);
}

/// <summary>
/// Writes one property of one component, as the inspector's fields send.
/// </summary>
/// <param name="ObjectId">The object that owns the component.</param>
/// <param name="ComponentTypeName">The component's type name, from the snapshot.</param>
/// <param name="PropertyName">The property's name.</param>
/// <param name="Text">The new value, in the inspector's text format.</param>
/// <remarks>
/// <para>
/// The command keeps the value it replaced, so Undo is exact and needs no re-parsing of the old text.
/// </para>
/// <para>
/// It can also be fed: while a field is being dragged the same command is given a new
/// <see cref="Text"/> and queued again, so the scene follows the pointer every frame. Only the first run
/// records the command and logs it, so a whole drag is one undo step and one console line — the same
/// compromise <c>PropertyDrawer</c> makes by recording on <c>IsItemDeactivatedAfterEdit</c>.
/// </para>
/// </remarks>
public sealed class SetPropertyCommand(
    Guid objectId,
    string componentTypeName,
    string propertyName,
    string text) : IUndoableCommand
{
    private string _text = text;
    private string? _before;
    private string? _after;
    private bool _recorded;

    /// <summary>
    /// The value to write. The UI thread swaps this while a drag feeds the same command again; the render
    /// thread reads it once per run.
    /// </summary>
    public string Text
    {
        get => Volatile.Read(ref _text);
        set => Volatile.Write(ref _text, value ?? string.Empty);
    }

    /// <inheritdoc />
    public string Describe => $"set {componentTypeName}.{propertyName}";

    /// <inheritdoc />
    public string? Execute(EditorCommandContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        if (FindComponent(context.Scene) is not { } component)
        {
            return $"[editor] could not {Describe}: the component is gone.";
        }

        if (!ComponentProperties.TryReadText(component, propertyName, out string before))
        {
            return $"[editor] could not {Describe}: no such property.";
        }

        if (!ComponentProperties.TryApply(component, propertyName, Text, out string? error))
        {
            return $"[editor] could not {Describe}: {error}";
        }

        ComponentProperties.TryReadText(component, propertyName, out string after);

        if (String.Equals(before, after, StringComparison.Ordinal))
        {
            // The text parsed to the value it replaced (or was clamped to it), so nothing happened.
            return null;
        }

        bool first = !_recorded;

        if (first)
        {
            // What undo must restore is the value before the first run of this command, never a value the
            // drag itself put there.
            _before = before;
            _recorded = true;
            context.History.Record(this);
        }

        _after = after;
        context.Scene.IsDirty = true;

        return first ? $"[editor] {componentTypeName}.{propertyName} = {after}" : null;
    }

    /// <inheritdoc />
    public string? Undo(EditorCommandContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        return Apply(context, _before, "restored");
    }

    /// <inheritdoc />
    public string? Redo(EditorCommandContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        return Apply(context, _after, "set");
    }

    private string? Apply(EditorCommandContext context, string? value, string verb)
    {
        if (value is null || FindComponent(context.Scene) is not { } component)
        {
            return null;
        }

        if (!ComponentProperties.TryApply(component, propertyName, value, out string? error))
        {
            return $"[editor] could not {verb} {Describe}: {error}";
        }

        context.Scene.IsDirty = true;

        return $"[editor] {verb} {componentTypeName}.{propertyName} = {value}";
    }

    private Component? FindComponent(Scene scene)
    {
        GameObject? target = scene.GameObjects.FirstOrDefault(gameObject => gameObject.Id == objectId);

        return target?.Components.FirstOrDefault(
            component => String.Equals(component.GetType().Name, componentTypeName, StringComparison.Ordinal));
    }
}

/// <summary>
/// Writes one of the object's transform rows: position, rotation (Euler degrees) or scale.
/// </summary>
/// <param name="ObjectId">The object.</param>
/// <param name="Field">Which row.</param>
/// <param name="Value">The three components the row shows.</param>
/// <remarks>
/// <para>
/// The parameter is <c>transformField</c> rather than <c>field</c> on purpose: C# 14 reads <c>field</c> inside a
/// property accessor as the property's own backing field, which silently turned <see cref="Describe"/> into a
/// read of an uninitialized string.
/// </para>
/// <para>
/// Like <see cref="SetPropertyCommand"/> it can be fed: a dragged row hands the same command a new
/// <see cref="Value"/> and queues it again, so the object moves every frame while the drag is one undo step.
/// The triple the drag started from is captured on the first run, never from a value the drag itself wrote.
/// </para>
/// </remarks>
public sealed class SetTransformCommand(Guid objectId, TransformField transformField, Vector3 value) : IUndoableCommand
{
    private readonly object _gate = new();
    private Vector3 _value = value;
    private Vector3 _beforePosition;
    private Quaternion _beforeRotation = Quaternion.Identity;
    private Vector3 _beforeScale;
    private bool _recorded;

    /// <summary>
    /// The row to write. The UI thread swaps this while a drag feeds the same command again; the render thread
    /// reads it once per run.
    /// </summary>
    public Vector3 Value
    {
        get
        {
            lock (_gate)
            {
                return _value;
            }
        }

        set
        {
            lock (_gate)
            {
                _value = value;
            }
        }
    }

    /// <inheritdoc />
    public string Describe => $"set transform {transformField.ToString().ToLowerInvariant()}";

    /// <inheritdoc />
    public string? Execute(EditorCommandContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        if (FindTarget(context.Scene) is not { } transform)
        {
            return $"[editor] could not {Describe}: that object is no longer in the scene.";
        }

        Vector3 target = Value;

        if ((transformField is TransformField.Position or TransformField.Scale) && Field(transform) == target)
        {
            return null;
        }

        bool first = !_recorded;

        if (first)
        {
            _beforePosition = transform.Position;
            _beforeRotation = transform.Rotation;
            _beforeScale = transform.Scale;
            _recorded = true;
        }

        ApplyField(transform, target);
        context.Scene.IsDirty = true;

        if (!first)
        {
            return null;
        }

        context.History.Record(this);

        return $"[editor] {Describe} = {Number(target.X)}, {Number(target.Y)}, {Number(target.Z)}";
    }

    /// <inheritdoc />
    public string? Undo(EditorCommandContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        if (FindTarget(context.Scene) is not { } transform)
        {
            return null;
        }

        transform.Position = _beforePosition;
        transform.Rotation = _beforeRotation;
        transform.Scale = _beforeScale;
        context.Scene.IsDirty = true;

        return $"[editor] restored {Describe}";
    }

    /// <inheritdoc />
    public string? Redo(EditorCommandContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        if (FindTarget(context.Scene) is not { } transform)
        {
            return null;
        }

        Vector3 target = Value;

        ApplyField(transform, target);
        context.Scene.IsDirty = true;

        return $"[editor] {Describe} = {Number(target.X)}, {Number(target.Y)}, {Number(target.Z)}";
    }

    private Vector3 Field(Transform transform) => transformField == TransformField.Position
        ? transform.Position
        : transform.Scale;

    private void ApplyField(Transform transform, Vector3 value)
    {
        switch (transformField)
        {
            case TransformField.Position:
                transform.Position = value;
                break;

            case TransformField.Rotation:
                // The row is Euler degrees, in the order the ImGui inspector writes them back.
                Vector3 radians = value * (MathF.PI / 180f);
                transform.Rotation = Quaternion.CreateFromYawPitchRoll(radians.Y, radians.X, radians.Z);
                break;

            default:
                transform.Scale = value;
                break;
        }
    }

    private Transform? FindTarget(Scene scene) =>
        scene.GameObjects.FirstOrDefault(gameObject => gameObject.Id == objectId)?.Transform;

    private static string Number(float number) => number.ToString("0.###", System.Globalization.CultureInfo.InvariantCulture);
}

/// <summary>
/// Adds a lua script to an object — what the inspector's Add ▾ menu sends when the typed name matches no
/// component, the way Unity offers to make a script.
/// </summary>
/// <param name="ObjectId">The object to add the script to.</param>
/// <param name="ScriptName">The name the user typed; a <c>.lua</c> extension and surrounding space are forgiven.</param>
/// <remarks>
/// The file is written once and then left alone: undoing removes the component, not the script, so work done in
/// an external editor survives. Attaching a name whose file already exists reuses that file.
/// </remarks>
public sealed class AddScriptCommand(Guid objectId, string scriptName) : IUndoableCommand
{
    private LanguageScript? _added;
    private string _path = string.Empty;

    /// <inheritdoc />
    public string Describe => $"add script {scriptName}";

    /// <inheritdoc />
    public string? Execute(EditorCommandContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        if (FindTarget(context.Scene) is not { } target)
        {
            return $"[editor] could not {Describe}: that object is no longer in the scene.";
        }

        if (ScriptTemplate.Normalize(scriptName) is { } name)
        {
            string candidate = ScriptTemplate.RelativePath(name);

            if (target.Components.OfType<LanguageScript>().Any(script =>
                    String.Equals(script.SourcePath, candidate, StringComparison.OrdinalIgnoreCase)))
            {
                return $"[editor] {target.Name} already has the script {candidate}.";
            }
        }

        if (!ScriptTemplate.TryCreate(context.ProjectRoot, scriptName, out string path, out string? error))
        {
            return $"[editor] could not {Describe}: {error}";
        }

        _path = path;
        _added = new LanguageScript { Language = ScriptTemplate.Language, SourcePath = _path };
        target.AddComponent(_added);

        context.Scene.IsDirty = true;
        context.History.Record(this);

        return $"[editor] added script {_path} to {target.Name}.";
    }

    /// <inheritdoc />
    public string? Undo(EditorCommandContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        if (_added is null || FindTarget(context.Scene) is not { } target)
        {
            return null;
        }

        target.RemoveComponent(_added);
        context.Scene.IsDirty = true;

        // The file stays: it is the user's, and another object may use it.
        return $"[editor] removed script {_path} from {target.Name}; the file is still there.";
    }

    /// <inheritdoc />
    public string? Redo(EditorCommandContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        if (_added is null || FindTarget(context.Scene) is not { } target)
        {
            return null;
        }

        target.AddComponent(_added);
        context.Scene.IsDirty = true;

        return $"[editor] added script {_path} back to {target.Name}.";
    }

    private GameObject? FindTarget(Scene scene) =>
        scene.GameObjects.FirstOrDefault(gameObject => gameObject.Id == objectId);
}

/// <summary>Renames an object.</summary>
/// <param name="ObjectId">The object.</param>
/// <param name="Name">The new name.</param>
public sealed class RenameObjectCommand(Guid objectId, string name) : IUndoableCommand
{
    private string? _before;

    /// <inheritdoc />
    public string Describe => $"rename to {name}";

    /// <inheritdoc />
    public string? Execute(EditorCommandContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        if (FindTarget(context.Scene) is not { } target)
        {
            return "[editor] could not rename: that object is no longer in the scene.";
        }

        string trimmed = (name ?? string.Empty).Trim();

        if (trimmed.Length == 0)
        {
            return "[editor] could not rename: a name cannot be empty.";
        }

        _before = target.Name;
        target.Name = trimmed;
        context.Scene.IsDirty = true;
        context.History.Record(this);

        return $"[editor] renamed {_before} to {trimmed}.";
    }

    /// <inheritdoc />
    public string? Undo(EditorCommandContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        if (_before is null || FindTarget(context.Scene) is not { } target)
        {
            return null;
        }

        target.Name = _before;
        context.Scene.IsDirty = true;

        return $"[editor] renamed {name} back to {_before}.";
    }

    /// <inheritdoc />
    public string? Redo(EditorCommandContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        if (FindTarget(context.Scene) is not { } target)
        {
            return null;
        }

        target.Name = name.Trim();
        context.Scene.IsDirty = true;

        return $"[editor] renamed to {name.Trim()}.";
    }

    private GameObject? FindTarget(Scene scene) =>
        scene.GameObjects.FirstOrDefault(gameObject => gameObject.Id == objectId);
}

/// <summary>Takes the newest change back.</summary>
public sealed class UndoCommand : IEditorCommand
{
    /// <inheritdoc />
    public string Describe => "undo";

    /// <inheritdoc />
    public string? Execute(EditorCommandContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        return context.History.TakeUndo() is { } command
            ? command.Undo(context)
            : "[editor] nothing to undo.";
    }
}

/// <summary>Re-applies the newest undone change.</summary>
public sealed class RedoCommand : IEditorCommand
{
    /// <inheritdoc />
    public string Describe => "redo";

    /// <inheritdoc />
    public string? Execute(EditorCommandContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        return context.History.TakeRedo() is { } command
            ? command.Redo(context)
            : "[editor] nothing to redo.";
    }
}
