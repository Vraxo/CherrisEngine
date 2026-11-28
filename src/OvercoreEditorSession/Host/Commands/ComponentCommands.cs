using Overcore.Components;
using Overcore.Core;
using Overcore.Scripting;
using Overcore.Serialization;
using Overcore.Utils;
using System.Collections.Concurrent;
using System.Numerics;

namespace OvercoreEditorSession.Host;

/// <summary>
/// Adds a component to an object in the live scene — what the inspector's Add Component menu sends.
/// </summary>
/// <param name="ObjectId">The object to add to.</param>
/// <param name="ComponentName">A display name from the menu: a built-in component's, or a compiled script's.</param>
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

        if (FindComponent(context, componentName) is not { } entry)
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

    /// <summary>
    /// The entry a display name refers to: the built-in catalog first, then the project's compiled scripts.
    /// </summary>
    /// <remarks>
    /// Built-ins win a name collision. A script called <c>Camera</c> is an odd thing to write, and the built-in one
    /// is what every scene already means by that name.
    /// </remarks>
    private static ComponentCatalog.Entry? FindComponent(EditorCommandContext context, string componentName) =>
        ComponentCatalog.Find(componentName) ??
        context.ScriptComponents.FirstOrDefault(entry =>
            String.Equals(entry.DisplayName, componentName, StringComparison.Ordinal));
}

/// <summary>
/// Switches a component on or off, as the inspector's header checkbox sends: Unity's enable toggle.
/// </summary>
/// <param name="ObjectId">The object that owns the component.</param>
/// <param name="ComponentTypeName">The component's type name, from the snapshot.</param>
/// <param name="Enabled">Where the checkbox goes.</param>
/// <remarks>
/// <para>
/// Off stops the component without taking it off the object — a renderer stops drawing, a light stops lighting,
/// a script stops ticking — and the state is saved with the scene (<see cref="Component.Enabled"/>).
/// </para>
/// <para>
/// A checkbox is one click, so there is nothing to feed: Undo and Redo put the toggle back and forward.
/// </para>
/// </remarks>
public sealed class SetComponentEnabledCommand(Guid objectId, string componentTypeName, bool enabled) : IUndoableCommand
{
    /// <inheritdoc />
    public string Describe => $"{(enabled ? "enable" : "disable")} {componentTypeName}";

    /// <inheritdoc />
    public string? Execute(EditorCommandContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        if (FindComponent(context.Scene) is not { } component)
        {
            return $"[editor] could not {Describe}: the component is gone.";
        }

        if (component.Enabled == enabled)
        {
            // Already where the checkbox wants it.
            return null;
        }

        string? line = Apply(context.Scene, component, enabled);
        context.History.Record(this);

        return line;
    }

    /// <inheritdoc />
    public string? Undo(EditorCommandContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        return FindComponent(context.Scene) is { } component ? Apply(context.Scene, component, !enabled) : null;
    }

    /// <inheritdoc />
    public string? Redo(EditorCommandContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        return FindComponent(context.Scene) is { } component ? Apply(context.Scene, component, enabled) : null;
    }

    /// <summary>
    /// Writes the toggle, then refreshes what the renderer treats as the main camera, the skybox and the lights:
    /// a camera or light that was just switched off must stop being one, and the next in the scene takes over.
    /// </summary>
    private static string? Apply(Scene scene, Component component, bool value)
    {
        component.Enabled = value;
        scene.FindMainComponents();
        scene.IsDirty = true;

        return $"[editor] {component.GetType().Name} on {component.GameObject?.Name ?? "nothing"} {(value ? "enabled" : "disabled")}.";
    }

    private Component? FindComponent(Scene scene)
    {
        GameObject? target = scene.GameObjects.FirstOrDefault(gameObject => gameObject.Id == objectId);

        return target?.Components.FirstOrDefault(
            component => String.Equals(component.GetType().Name, componentTypeName, StringComparison.Ordinal));
    }
}
