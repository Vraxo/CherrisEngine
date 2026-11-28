using Overcore.Components;
using Overcore.Core;
using Overcore.Scripting;
using Overcore.Serialization;
using Overcore.Utils;
using System.Collections.Concurrent;
using System.Numerics;

namespace OvercoreEditorSession.Host;

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
