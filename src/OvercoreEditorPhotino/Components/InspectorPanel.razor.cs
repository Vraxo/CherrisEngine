using System.Globalization;
using System.Numerics;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Routing;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.Extensions.Logging;
using Microsoft.JSInterop;
using Overcore.Projects;
using OvercoreEditorPhotino;
using OvercoreEditorPhotino.Components;
using OvercoreEditorPhotino.Shared;
using OvercoreEditorPhotino.Services;
using Overcore.Components;
using Overcore.Scripting;
using OvercoreEditorSession;
using OvercoreEditorSession.Host;

namespace OvercoreEditorPhotino.Components;

/// <summary>
/// Code-behind for <see cref="InspectorPanel"/>: the inspector's edit state and commands.
/// The markup lives in <c>InspectorPanel.razor</c>; this file holds the pending-edit tracking,
/// display formatting and host calls so the razor file stays a view.
/// </summary>
public partial class InspectorPanel
{
    private Guid? _addMenuFor;

    /// <summary>Which object's Overrides dropdown is open (Unity's), or null.</summary>
    private Guid? _overridesFor;

    private string _search = string.Empty;
    private ElementReference _searchBox;
    private bool _focusSearch;
    private HashSet<string> _collapsed = new(StringComparer.Ordinal);
    private readonly PendingComponentToggles _pendingToggles = new();
    private readonly PendingResetStates _pendingResets = new();
    private bool _disposed;

    private SceneNodeSnapshot? SelectedNode
    {
        get
        {
            if (Selection.SelectedId is not Guid id || Store.Current is not SceneSnapshot scene)
            {
                return null;
            }

            return scene.Nodes.FirstOrDefault(node => node.Id == id);
        }
    }

    /// <summary>True while a session is running to apply an edit to.</summary>
    private bool CanEdit => Host.State == EditorSessionState.Running;

    private HashSet<string> _editedFields = [];
    private int _editRevision;

    /// <summary>
    /// The key of a field: the field's own id, except for the one an edit came from, which changes so Blazor
    /// rebuilds it when the next snapshot arrives. That is how a refused value goes back to what the engine
    /// actually holds — an input's <c>value</c> attribute alone cannot, because the value it would be set back
    /// to is the one already in the render tree. Only the edited field is rebuilt, so nothing else loses focus.
    /// </summary>
    private string KeyFor(string field) => _editedFields.Contains(field)
        ? field + "#" + _editRevision.ToString(CultureInfo.InvariantCulture)
        : field;

    /// <summary>
    /// Makes the named fields new elements on the next render, so the snapshot's value shows even when the
    /// element holds text the author typed. The revision in each key is what makes them new.
    /// </summary>
    /// <param name="fields">Field keys, the same ones <see cref="KeyFor"/> is given.</param>
    private void Touched(params string[] fields)
    {
        _editedFields = [.. fields];
        _editRevision++;
    }

    private static string PropertyKey(SceneNodeSnapshot node, ComponentSnapshot component, PropertySnapshot property) =>
        node.Id.ToString() + ":" + component.TypeName + "." + property.Name;

    private static string TransformKey(Guid objectId, TransformRow row) =>
        objectId.ToString() + ":transform." + row.Field;

    /// <summary>The reset button's state: the user's own answer while its command is unconfirmed.</summary>
    private bool ResetState(string key, bool snapshotCanReset) => _pendingResets.Value(key, snapshotCanReset);

    /// <summary>The frame of the newest snapshot the panel has, which every edit is made against.</summary>
    private long CurrentFrame => Store.Current?.Frame ?? -1;

    /// <summary>Records an edit's button state at once, so it shows before the next snapshot is polled.</summary>
    private long BeginReset(string key, bool canReset) => _pendingResets.Begin(key, canReset, CurrentFrame);

    /// <summary>
    /// The text a scalar field shows: the value a reset is putting back while it is unconfirmed, and the
    /// snapshot's own text the rest of the time.
    /// </summary>
    private string DisplayText(string key, PropertySnapshot property) =>
        _pendingResets.Text(key) ?? property.Text;

    /// <summary>One axis of a vector field, split out of the value <see cref="DisplayText"/> reports.</summary>
    private string DisplayAxisText(string key, PropertySnapshot property, int axis)
    {
        if (_pendingResets.Text(key) is { } pending)
        {
            // A vector's text is the components separated by ", ", the format an edit sends back.
            string[] parts = pending.Split(", ");
            if (axis < parts.Length)
            {
                return parts[axis];
            }
        }

        return Format(property.Components[axis]);
    }

    /// <summary>
    /// One axis of a transform row: the whole row reads the default while a reset is unconfirmed, because
    /// <see cref="SceneNodeSnapshot"/> carries the three components and the edit writes all three.
    /// </summary>
    private string DisplayTransformText(string key, TransformRow row, int axis)
    {
        if (_pendingResets.Text(key) is not null)
        {
            return Format(axis switch
            {
                0 => row.Default.X,
                1 => row.Default.Y,
                _ => row.Default.Z
            });
        }

        return Format(row.Components[axis]);
    }

    /// <summary>The text a toggle or a dropdown shows, so a reset moves it with the arrow.</summary>
    private string DisplayChoiceText(string key, PropertySnapshot property) =>
        _pendingResets.Text(key) ?? property.Text;

    /// <summary>True when a toggle is on, reading the value a reset is putting back when there is one.</summary>
    private bool DisplayIsOn(string key, PropertySnapshot property) =>
        String.Equals(DisplayChoiceText(key, property), "true", StringComparison.OrdinalIgnoreCase);

    /// <summary>A colour's swatch, from the components a reset is putting back when there is one.</summary>
    private string DisplayHex(string key, PropertySnapshot property)
    {
        if (_pendingResets.Text(key) is { } pending)
        {
            string[] parts = pending.Split(", ");
            float[] components = new float[3];
            for (int axis = 0; axis < 3; axis++)
            {
                components[axis] = axis < parts.Length && Single.TryParse(
                    parts[axis], NumberStyles.Float, CultureInfo.InvariantCulture, out float value)
                    ? value
                    : property.Components[axis];
            }

            return Hex(components);
        }

        return Hex(property.Components);
    }

    /// <summary>
    /// True when the new text is away from the default, which is when resetting it does something. A toggle and a
    /// dropdown are named, so only their casing may differ: a checkbox hands back <c>True</c> for <c>true</c>.
    /// </summary>
    private static bool CanResetText(PropertySnapshot property, string newText) =>
        property.DefaultText is not null && !String.Equals(
            newText,
            property.DefaultText,
            property.Kind is PropertyKind.Boolean or PropertyKind.Choice
                ? StringComparison.OrdinalIgnoreCase
                : StringComparison.Ordinal);

    /// <summary>True when the new transform row is away from its default, with <see cref="TransformRow"/>'s tolerance.</summary>
    private static bool CanResetTransform(TransformRow row, Vector3 updated) =>
        MathF.Abs(updated.X - row.Default.X) > 0.001f ||
        MathF.Abs(updated.Y - row.Default.Y) > 0.001f ||
        MathF.Abs(updated.Z - row.Default.Z) > 0.001f;

    /// <summary>
    /// Notes that a reset edit has run on the render thread, so snapshots from before it stop overriding the
    /// button. Runs on the render thread; the panel is marshalled back to the renderer.
    /// </summary>
    private void AcknowledgeReset(string key, long revision, long frame)
    {
        _ = InvokeAsync(() =>
        {
            if (_disposed)
            {
                return;
            }
            _pendingResets.Acknowledge(key, revision, frame);
            if (Store.Current is { } snapshot)
            {
                _pendingResets.Reconcile(snapshot.Frame);
            }

            // The render that follows shows what the engine now holds, without the user's guess standing in
            // front of it: the arrow and the field both come from the snapshot again.
            StateHasChanged();
        });
    }

    private string AddTitle => CanEdit
        ? "Add a component to this object"
        : "No render session is running";

    private bool IsAddMenuOpen(Guid objectId) => CanEdit && _addMenuFor == objectId;

    /// <summary>True when the named section is folded shut. The key is the object and the component type.</summary>
    private bool IsCollapsed(string key) => _collapsed.Contains(key);

    /// <summary>Folds or unfolds a section, the way a Unity foldout header does.</summary>
    private void ToggleSection(string key)
    {
        if (!_collapsed.Remove(key))
        {
            _collapsed.Add(key);
        }
    }

    private void ToggleAddMenu(Guid objectId)
    {
        if (IsAddMenuOpen(objectId))
        {
            CloseAddMenu();
            return;
        }

        if (!CanEdit)
        {
            return;
        }

        _addMenuFor = objectId;
        _search = string.Empty;

        // The box does not exist until the menu has rendered, so the focus is taken after the render.
        _focusSearch = true;
    }

    private void CloseAddMenu()
    {
        _addMenuFor = null;
        _search = string.Empty;
        _focusSearch = false;
    }

    /// <summary>
    /// A number field lost the focus: the drag is over, so the next edit starts its own undo step, and the field
    /// is rebuilt from the snapshot — which is what puts a refused value back to what the engine holds.
    /// </summary>
    private void Blurred(string field)
    {
        Host.EndLiveEdit();
        Touched(field);
    }

    /// <summary>Ends a drag without rebuilding: a keystroke means the next typed value is its own step.</summary>
    private void EndLiveEdit() => Host.EndLiveEdit();

    /// <summary>
    /// Puts a property back to the value a freshly constructed component of its type has — the same default the
    /// ImGui inspector's reset button restores. It is an ordinary edit, so it undoes in one step.
    /// </summary>
    /// <param name="node">The object that owns the component.</param>
    /// <param name="component">The component.</param>
    /// <param name="property">The property to put back.</param>
    private void ResetProperty(
        SceneNodeSnapshot node, ComponentSnapshot component, PropertySnapshot property)
    {
        if (property.DefaultText is not { } text)
        {
            return;
        }

        // Show the reset at once; the snapshot confirms it a few frames later.
        string propertyKey = PropertyKey(node, component, property);
        long revision = _pendingResets.Begin(propertyKey, canReset: false, CurrentFrame, text);

        if (!Host.SetProperty(node.Id, component.TypeName, property.Name, text,
                frame => AcknowledgeReset(propertyKey, revision, frame)))
        {
            _pendingResets.Cancel(propertyKey, revision);
        }

        if (property.Kind is not (PropertyKind.Vector2 or PropertyKind.Vector3))
        {
            // A number has a field and a slider, so both are replaced; the others have one element.
            Touched(propertyKey, propertyKey + ".slider", propertyKey + ".swatch");
            return;
        }

        // A vector is one key per axis, and a colour is also a swatch, so every part of the row is rebuilt.
        string[] fields =
        [
            .. TransformRow.Axes.Take(property.Components.Count).Select(axis => propertyKey + "." + axis),
            propertyKey + ".swatch"
        ];

        Touched(fields);
    }

    /// <summary>Puts a transform row back to zero, the identity or one: what a new object starts with.</summary>
    /// <param name="objectId">The object to move.</param>
    /// <param name="row">The row the button belongs to.</param>
    private void ResetTransform(Guid objectId, TransformRow row)
    {
        if (!row.CanReset)
        {
            return;
        }

        string resetKey = TransformKey(objectId, row);
        long revision = _pendingResets.Begin(resetKey, canReset: false, CurrentFrame, row.DefaultText);

        if (!Host.SetTransform(objectId, row.Field, row.Default.X, row.Default.Y, row.Default.Z,
                frame => AcknowledgeReset(resetKey, revision, frame)))
        {
            _pendingResets.Cancel(resetKey, revision);
        }

        string[] axes = [.. TransformRow.Axes.Select(axis => resetKey + "." + axis)];

        Touched(axes);
    }

    private void Search(string? text)
    {
        _search = text ?? string.Empty;
    }

    /// <summary>The entries this object could take whose name contains the search text.</summary>
    private IReadOnlyList<ComponentCatalog.Entry> Matches(SceneNodeSnapshot node)
    {
        string query = _search.Trim();

        return AddableTypes(node)
            .Where(entry => query.Length == 0 ||
                entry.DisplayName.Contains(query, StringComparison.OrdinalIgnoreCase))
            .ToArray();
    }

    /// <summary>True when the typed name could become a script the render thread would write into the project.</summary>
    private bool CanAddScript() => CanEdit && Host.HasProject;

    private string ScriptLabel(string query, string? scriptName, ScriptKind kind)
    {
        string language = ScriptTemplate.DisplayName(kind);

        if (scriptName is null)
        {
            return "New " + language + " script: \"" + query + "\"";
        }

        return Host.ScriptExists(scriptName, kind)
            ? "Script: \"" + scriptName + "\" (already in this project)"
            : "New " + language + " script: \"" + scriptName + "\"";
    }

    private string ScriptTitle(string query, string? scriptName, ScriptKind kind)
    {
        if (scriptName is null)
        {
            return $"\"{query}\" cannot name a script: start with a letter or underscore, then use letters, " +
                "digits and underscores.";
        }

        if (!Host.HasProject)
        {
            return "Open a project to create scripts: they live in the project's Scripts folder.";
        }

        string path = ScriptTemplate.RelativePath(scriptName, kind);

        if (Host.ScriptExists(scriptName, kind))
        {
            return "Attach the existing " + path + " to this object.";
        }

        return kind == ScriptKind.CSharp
            ? "Create " + path + ", compile it, and attach the component it declares to this object."
            : "Create " + path + " with an empty start() and update(), then attach it to this object.";
    }

    /// <summary>
    /// Enter takes the first match, or the Lua script suggestion when nothing matched — the first row, which is what
    /// the menu shows; Escape closes.
    /// </summary>
    private void OnSearchKeyDown(KeyboardEventArgs e)
    {
        if (e.Key == "Escape")
        {
            CloseAddMenu();
            return;
        }

        if (e.Key != "Enter" || SelectedNode is not { } node || !IsAddMenuOpen(node.Id))
        {
            return;
        }

        IReadOnlyList<ComponentCatalog.Entry> matches = Matches(node);

        if (matches.Count > 0)
        {
            AddComponent(node, matches[0]);
        }
        else if (CanAddScript() && ScriptTemplate.Normalize(_search) is not null)
        {
            AddScript(node, ScriptKind.Lua);
        }
    }

    private void AddScript(SceneNodeSnapshot node, ScriptKind kind)
    {
        if (ScriptTemplate.Normalize(_search) is not { } name)
        {
            return;
        }

        CloseAddMenu();

        if (kind == ScriptKind.CSharp)
        {
            Host.AddCSharpScript(node.Id, name);
        }
        else
        {
            Host.AddScript(node.Id, name);
        }
    }

    /// <summary>Takes the focus once the box is on screen: an inline menu cannot use the autofocus attribute.</summary>
    protected override async Task OnAfterRenderAsync(bool firstRender)
    {
        if (!_focusSearch)
        {
            return;
        }

        _focusSearch = false;

        try
        {
            await _searchBox.FocusAsync();
        }
        catch (Exception exception) when (exception is InvalidOperationException or JSDisconnectedException)
        {
            // No element to focus and no browser to focus it in; the menu still works with the mouse.
        }
    }

    /// <summary>The catalog entries this object does not have yet: an object holds one of each type.</summary>
    /// <remarks>
    /// The list comes from the host rather than from the catalog: a project's C# scripts are components too, and
    /// they only exist once the session has compiled them.
    /// </remarks>
    private IReadOnlyList<ComponentCatalog.Entry> AddableTypes(SceneNodeSnapshot node) =>
        Host.ComponentTypes
            .Where(entry => !node.Components.Any(component =>
                String.Equals(component.TypeName, entry.ComponentType.Name, StringComparison.Ordinal)))
            .ToArray();

    private void AddComponent(SceneNodeSnapshot node, ComponentCatalog.Entry entry)
    {
        CloseAddMenu();
        Host.AddComponent(node.Id, entry.DisplayName);
    }

    private void Rename(SceneNodeSnapshot node, string? name)
    {
        if (name is null || String.Equals(name, node.Name, StringComparison.Ordinal))
        {
            return;
        }

        Host.RenameObject(node.Id, name);
        Touched(node.Id.ToString() + ":name");
    }

    // ---- prefab overrides ----

    /// <summary>Whether one value of this object has drifted from its prefab source.</summary>
    private static bool IsOverridden(SceneNodeSnapshot node, string propertyPath) =>
        node.Overrides.Any(entry => String.Equals(entry.PropertyPath, propertyPath, StringComparison.Ordinal));

    private void ToggleOverridesMenu(SceneNodeSnapshot node) =>
        _overridesFor = _overridesFor == node.Id ? null : node.Id;

    private void CloseOverridesMenu() => _overridesFor = null;

    private void RevertOverride(SceneNodeSnapshot node, PrefabOverrideSnapshot entry)
    {
        CloseOverridesMenu();
        Host.RevertOverride(node.Id, entry.PropertyPath);
    }

    private void ApplyOverride(SceneNodeSnapshot node, PrefabOverrideSnapshot entry)
    {
        CloseOverridesMenu();
        Host.ApplyOverride(node.Id, entry.PropertyPath);
    }

    private void RevertAllOverrides(SceneNodeSnapshot node)
    {
        CloseOverridesMenu();
        Host.RevertAllOverrides(node.Id, node.Name);
    }

    private void ApplyAllOverrides(SceneNodeSnapshot node)
    {
        CloseOverridesMenu();
        Host.ApplyAllOverrides(node.Id, node.Name);
    }

    /// <summary>
    /// The header checkbox: show the user's choice immediately, then use the first snapshot made after the
    /// command completed. This prevents an older snapshot from briefly undoing the click on screen.
    /// </summary>
    /// <param name="node">The object that owns the component.</param>
    /// <param name="component">The component.</param>
    /// <param name="enabled">Where the checkbox went.</param>
    private void SetComponentEnabled(SceneNodeSnapshot node, ComponentSnapshot component, bool enabled)
    {
        long revision = _pendingToggles.Begin(node.Id, component.TypeName, enabled);
        if (!Host.SetComponentEnabled(node.Id, component.TypeName, enabled, frame =>
        {
            _ = InvokeAsync(() =>
            {
                if (_disposed)
                {
                    return;
                }
                _pendingToggles.Acknowledge(node.Id, component.TypeName, revision, frame);
                if (Store.Current is { } snapshot)
                {
                    _pendingToggles.Reconcile(snapshot.Frame);
                }
                StateHasChanged();
            });
        }))
        {
            _pendingToggles.Cancel(node.Id, component.TypeName, revision);
            // Force the browser's changed checkbox back to the unchanged snapshot if enqueueing failed.
            Touched(node.Id.ToString() + ":" + component.TypeName + ":enabled");
        }
    }

    private void OnTransformChanged(SceneNodeSnapshot node, TransformRow row, int axis, string? text)
    {
        if (!TryParse(text, out float value))
        {
            return;
        }

        Vector3 updated = row.Value;
        updated = axis switch
        {
            0 => new Vector3(value, updated.Y, updated.Z),
            1 => new Vector3(updated.X, value, updated.Z),
            _ => new Vector3(updated.X, updated.Y, value)
        };

        // Show the reset button at once; the snapshot confirms it a few frames later.
        string resetKey = TransformKey(node.Id, row);
        long revision = BeginReset(resetKey, CanResetTransform(row, updated));

        // Live: the row moves with the pointer, and the whole drag is one undo step.
        if (!Host.SetTransformLive(node.Id, row.Field, updated.X, updated.Y, updated.Z,
                frame => AcknowledgeReset(resetKey, revision, frame)))
        {
            _pendingResets.Cancel(resetKey, revision);
        }
    }

    /// <summary>A checkbox or a dropdown: one value, one step.</summary>
    private void OnPropertyChanged(
        SceneNodeSnapshot node, ComponentSnapshot component, PropertySnapshot property, string? text)
    {
        string newText = text ?? string.Empty;
        string propertyKey = PropertyKey(node, component, property);
        long revision = BeginReset(propertyKey, CanResetText(property, newText));

        if (!Host.SetProperty(node.Id, component.TypeName, property.Name, newText,
                frame => AcknowledgeReset(propertyKey, revision, frame)))
        {
            _pendingResets.Cancel(propertyKey, revision);
        }

        Touched(propertyKey);
    }

    /// <summary>A number the pointer is moving: applied every frame, recorded once.</summary>
    private void OnPropertyDragged(
        SceneNodeSnapshot node, ComponentSnapshot component, PropertySnapshot property, string? text)
    {
        string newText = text ?? string.Empty;
        string propertyKey = PropertyKey(node, component, property);
        long revision = BeginReset(propertyKey, CanResetText(property, newText));

        if (!Host.SetPropertyLive(node.Id, component.TypeName, property.Name, newText,
                frame => AcknowledgeReset(propertyKey, revision, frame)))
        {
            _pendingResets.Cancel(propertyKey, revision);
        }
    }

    /// <summary>One axis of a vector: the other components come from the snapshot, so the row stays whole.</summary>
    private void OnVectorChanged(
        SceneNodeSnapshot node, ComponentSnapshot component, PropertySnapshot property, int axis, string? text)
    {
        if (!TryParse(text, out float value))
        {
            return;
        }

        string[] parts = property.Components.Select(Format).ToArray();
        parts[axis] = Format(value);
        string newText = String.Join(", ", parts);

        string propertyKey = PropertyKey(node, component, property);
        long revision = BeginReset(propertyKey, CanResetText(property, newText));

        if (!Host.SetPropertyLive(node.Id, component.TypeName, property.Name, newText,
                frame => AcknowledgeReset(propertyKey, revision, frame)))
        {
            _pendingResets.Cancel(propertyKey, revision);
        }
    }

    /// <summary>A colour picker hands back #rrggbb; the property holds normalized components.</summary>
    private void OnColourChanged(
        SceneNodeSnapshot node, ComponentSnapshot component, PropertySnapshot property, string? hex)
    {
        if (hex is null || hex.Length != 7 || hex[0] != '#')
        {
            return;
        }

        string[] parts = new string[property.Components.Count];
        parts[0] = Format(Convert.ToInt32(hex.Substring(1, 2), 16) / 255f);
        parts[1] = Format(Convert.ToInt32(hex.Substring(3, 2), 16) / 255f);
        parts[2] = Format(Convert.ToInt32(hex.Substring(5, 2), 16) / 255f);
        for (int i = 3; i < parts.Length; i++)
        {
            parts[i] = Format(property.Components[i]);
        }

        string text = String.Join(", ", parts);

        string propertyKey = PropertyKey(node, component, property);
        long revision = BeginReset(propertyKey, CanResetText(property, text));

        if (!Host.SetProperty(node.Id, component.TypeName, property.Name, text,
                frame => AcknowledgeReset(propertyKey, revision, frame)))
        {
            _pendingResets.Cancel(propertyKey, revision);
        }
    }

    private static string Hex(IReadOnlyList<float> components)
    {
        static int Channel(float value) => (int)Math.Clamp(MathF.Round(value * 255f), 0f, 255f);

        return String.Format(
            CultureInfo.InvariantCulture, "#{0:x2}{1:x2}{2:x2}", Channel(components[0]), Channel(components[1]), Channel(components[2]));
    }

    private static string Format(float value) => value.ToString("0.###", CultureInfo.InvariantCulture);

    /// <summary>
    /// Splits a PascalCase property or component name into the words Unity would show. An uppercase letter
    /// followed by a lowercase one starts a word; an acronym such as FOV stays one word. Short connecting
    /// words are lower-cased, so <c>FieldOfView</c> reads "Field of View" rather than "Field Of View".
    /// </summary>
    private static string DisplayName(string name)
    {
        string spaced = Regex.Replace(name, "(?<!^)([A-Z])(?=[a-z])", " $1");
        return Regex.Replace(spaced, @"\b(Of|The|And|To|In|On)\b", match => match.Value.ToLowerInvariant());
    }

    /// <summary>
    /// The Font Awesome glyph and theme colour for each component, from the mapping the hierarchy's rows also
    /// use (<see cref="ComponentGlyphs"/>).
    /// </summary>
    private static (string Glyph, string Color) IconFor(string typeName) => ComponentGlyphs.For(typeName);

    /// <summary>Compiled C# scripts get the script glyph even though their class names are project-specific.</summary>
    private static (string Glyph, string Color) IconFor(Type componentType) => ComponentGlyphs.For(componentType);

    private static string IconClass(string typeName) => IconFor(typeName).Glyph;

    private static string IconClass(Type componentType) => IconFor(componentType).Glyph;

    private static string IconColorClass(string typeName) => IconFor(typeName).Color;

    private static string IconColorClass(Type componentType) => IconFor(componentType).Color;

    /// <summary>A bound as an attribute value: empty means "none", which the drag script reads as no clamp.</summary>
    private static string RangeText(float? bound) => bound is float value ? Format(value) : string.Empty;

    private static bool TryParse(string? text, out float value) =>
        Single.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out value);

    protected override void OnInitialized()
    {
        Store.Changed += OnSceneChanged;
        Selection.Changed += OnSceneChanged;
    }

    private void OnSceneChanged()
    {
        _ = InvokeAsync(() =>
        {
            if (_disposed)
            {
                return;
            }
            if (Store.Current is { } snapshot)
            {
                _pendingToggles.Reconcile(snapshot.Frame);
                _pendingResets.Reconcile(snapshot.Frame);
            }
            StateHasChanged();
        });
    }

    public void Dispose()
    {
        _disposed = true;
        Store.Changed -= OnSceneChanged;
        Selection.Changed -= OnSceneChanged;
    }
}
