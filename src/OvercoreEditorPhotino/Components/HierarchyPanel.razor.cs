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
using OvercoreEditorSession;
using Overcore.Components;
using Overcore.Scripting;

namespace OvercoreEditorPhotino.Components;

/// <summary>
/// Code-behind for <see cref="HierarchyPanel"/>: the tree flattening, search and selection state.
/// The markup lives in <c>HierarchyPanel.razor</c>; this file holds the state and handlers.
/// </summary>
public partial class HierarchyPanel
{
    private readonly HashSet<Guid> _collapsed = [];
    private string _search = string.Empty;
    private List<Row> _rows = [];
    private bool _sceneExpanded = true;
    private bool _createMenuOpen;

    // The context menu: open or not, where it was opened, and the row it was opened on (null over the scene
    // row or empty space, where only creating is offered).
    private bool _menuOpen;
    private double _menuX;
    private double _menuY;
    private Row? _menuRow;

    // The inline rename the menu opens: which row, what the field says, and whether it should take focus.
    private Guid? _renamingId;
    private string _renameText = string.Empty;
    private bool _focusRename;
    private ElementReference _renameInput;

    /// <summary>One drawn object row: where it sits, what it shows, and what its visibility toggle drives.</summary>
    /// <param name="Node">The object.</param>
    /// <param name="Depth">Tree depth, one indent step each; the scene row over the rows is depth zero.</param>
    /// <param name="HasChildren">Whether the row owns a foldout at all.</param>
    /// <param name="Expanded">Whether its children are showing.</param>
    /// <param name="Icon">The glyph and tint of the row's first component — the one icon Unity shows per row.</param>
    /// <param name="VisibilityType">
    /// The component the row's eye switches, or null when the object draws nothing. Unity only leaves the
    /// visibility toggle on rows that can be hidden from view; Overcore can only hide what draws.
    /// </param>
    /// <param name="Hidden">Whether that component is currently switched off.</param>
    private sealed record Row(
        SceneNodeSnapshot Node,
        int Depth,
        bool HasChildren,
        bool Expanded,
        (string Glyph, string Color) Icon,
        string? VisibilityType,
        bool Hidden);

    private bool HasScene => Store.Current is not null;

    private bool CanEdit => Host.State == EditorSessionState.Running;

    private string CreateTitle => CanEdit
        ? "Add an empty object at the scene root"
        : "No render session is running";

    /// <summary>The scene row's label: the scene's own name, the way Unity titles the root row.</summary>
    private string SceneName => Store.Current?.SceneName is { Length: > 0 } name ? name : "Untitled Scene";

    protected override void OnInitialized()
    {
        Store.Changed += OnSceneChanged;

        // A click in the viewport selects on the render thread; the highlight has to follow it straight away.
        Selection.Changed += OnSelectionChanged;
        Rebuild();
    }

    private void OnSelectionChanged()
    {
        _ = InvokeAsync(StateHasChanged);
    }

    private void OnSceneChanged()
    {
        _ = InvokeAsync(() =>
        {
            Rebuild();
            StateHasChanged();
        });
    }

    private void Rebuild()
    {
        List<Row> rows = [];

        // A search keeps every branch open; a collapsed scene row folds the whole list away.
        if (_sceneExpanded || IsSearching)
        {
            foreach (SceneTreeNode root in Store.Roots)
            {
                // Depth one: the scene row over these rows is the root everything hangs under, like Unity's.
                AddNode(root, 1, rows);
            }
        }

        _rows = rows;
    }

    private void AddNode(SceneTreeNode node, int depth, List<Row> rows)
    {
        if (!IsVisible(node))
        {
            return;
        }

        bool hasChildren = node.Children.Count > 0;
        bool expanded = IsExpanded(node.Node.Id);

        rows.Add(new Row(
            node.Node,
            depth,
            hasChildren,
            expanded,
            RowIcon(node.Node),
            VisibilityType(node.Node),
            IsHidden(node.Node)));

        if (!expanded || !hasChildren)
        {
            return;
        }

        foreach (SceneTreeNode child in node.Children)
        {
            AddNode(child, depth + 1, rows);
        }
    }

    /// <summary>Unity shows one icon per row: the face of the object's first component after the transform.</summary>
    private static (string Glyph, string Color) RowIcon(SceneNodeSnapshot node)
    {
        ComponentSnapshot? first = node.Components.FirstOrDefault(
            component => !String.Equals(component.TypeName, "Transform", StringComparison.Ordinal));

        // An empty object gets the plain grey cube, the way Unity marks a bare GameObject.
        return first is null ? ("fa-cube", "hier-icon-plain") : ComponentGlyphs.For(first.TypeName);
    }

    /// <summary>
    /// The component the visibility toggle drives — the renderer that draws the object — or null when the
    /// object draws nothing and Unity would leave the eye cell empty.
    /// </summary>
    private static string? VisibilityType(SceneNodeSnapshot node) =>
        node.Components.FirstOrDefault(
            component => String.Equals(component.TypeName, "MeshRenderer", StringComparison.Ordinal))?.TypeName;

    /// <summary>The eye reads the state the renderer is in now, from the same snapshot the row draws.</summary>
    private static bool IsHidden(SceneNodeSnapshot node) =>
        node.Components.FirstOrDefault(
            component => String.Equals(component.TypeName, "MeshRenderer", StringComparison.Ordinal)) is { Enabled: false };

    private bool IsSearching => !string.IsNullOrWhiteSpace(_search);

    // A search keeps a node when it matches itself or has a matching descendant, like ImGui's filter.
    private bool IsVisible(SceneTreeNode node)
    {
        if (!IsSearching)
        {
            return true;
        }

        return node.Node.Name.Contains(_search, StringComparison.OrdinalIgnoreCase)
            || node.Children.Any(IsVisible);
    }

    private bool IsExpanded(Guid id)
    {
        // Matches stay expanded so a filtered tree is readable without extra clicks.
        return IsSearching || !_collapsed.Contains(id);
    }

    private void Toggle(Guid id)
    {
        if (!_collapsed.Remove(id))
        {
            _collapsed.Add(id);
        }

        Rebuild();
    }

    private void ToggleScene()
    {
        _sceneExpanded = !_sceneExpanded;

        Rebuild();
    }

    private void ToggleVisible(Row row, string visibilityType)
    {
        // The eye shows the state the object is in; the click asks for the other one.
        Host.SetComponentEnabled(row.Node.Id, visibilityType, row.Hidden);
    }

    private void ToggleCreateMenu() => _createMenuOpen = !_createMenuOpen;

    private void CloseCreateMenu() => _createMenuOpen = false;

    private void CreateEmptyObject()
    {
        CloseCreateMenu();

        // Unity names an empty "GameObject", and selects it the moment it appears.
        if (Host.CreateGameObject("GameObject", out Guid objectId))
        {
            Selection.Select(objectId);
        }
    }

    // ---- context menu ----

    /// <summary>The menu hangs at the pointer, clamped to the window so it never opens off screen.</summary>
    private string MenuStyle =>
        $"left: min({_menuX.ToString(CultureInfo.InvariantCulture)}px, calc(100vw - 220px)); " +
        $"top: min({_menuY.ToString(CultureInfo.InvariantCulture)}px, calc(100vh - 160px))";

    private void OpenRowMenu(MouseEventArgs args, Row row)
    {
        _menuX = args.ClientX;
        _menuY = args.ClientY;
        _menuRow = row;
        _menuOpen = true;

        // Unity selects on right-click too, so the menu's items act on a highlighted row.
        Selection.Select(row.Node.Id);
    }

    private void OpenTreeMenu(MouseEventArgs args)
    {
        _menuX = args.ClientX;
        _menuY = args.ClientY;
        _menuRow = null;
        _menuOpen = true;
    }

    private void CloseMenu() => _menuOpen = false;

    private void CreateFromMenu()
    {
        CloseMenu();
        CreateEmptyObject();
    }

    private string RowActionTitle(string action) => CanEdit ? action : "No render session is running";

    private void BeginRename(Row row)
    {
        CloseMenu();
        _renamingId = row.Node.Id;
        _renameText = row.Node.Name;
        _focusRename = true;
    }

    private void DeleteFromMenu(Row row)
    {
        CloseMenu();

        // The row and its subtree leave together, so a selection anywhere inside would point at nothing.
        if (IsInSubtree(Selection.SelectedId, row))
        {
            Selection.Select(null);
        }

        Host.DeleteGameObject(row.Node.Id, row.Node.Name);
    }

    private void CreatePrefabFromMenu(Row row)
    {
        CloseMenu();
        Host.CreatePrefab(row.Node.Id, row.Node.Name);
    }

    /// <summary>Whether the selection sits on or under the row: the row and its subtree leave together.</summary>
    private bool IsInSubtree(Guid? selectedId, Row root)
    {
        if (selectedId is not Guid id)
        {
            return false;
        }

        if (id == root.Node.Id)
        {
            return true;
        }

        // Rows of the subtree run right after the root, one level deeper each.
        for (int i = _rows.IndexOf(root) + 1; i < _rows.Count && _rows[i].Depth > root.Depth; i++)
        {
            if (_rows[i].Node.Id == id)
            {
                return true;
            }
        }

        return false;
    }

    // ---- inline rename ----

    protected override async Task OnAfterRenderAsync(bool firstRender)
    {
        if (!_focusRename)
        {
            return;
        }

        _focusRename = false;

        try
        {
            // The field exists only once the row has rendered, so it takes focus here. A webview that will
            // not take focus is nothing to recover from: the keyboard still works.
            await _renameInput.FocusAsync();
        }
        catch (Exception exception) when (exception is JSDisconnectedException or InvalidOperationException)
        {
            // No interop to focus with: the window is closing, or the element is gone.
        }
    }

    private void OnRenameKey(KeyboardEventArgs args)
    {
        if (args.Key == "Enter")
        {
            CommitRename();
        }
        else if (args.Key == "Escape")
        {
            // Escape backs out without renaming; the blur that follows finds nothing to commit.
            _renamingId = null;
        }
    }

    private void CommitRename()
    {
        if (_renamingId is not Guid id)
        {
            return;
        }

        string text = _renameText.Trim();
        _renamingId = null;

        // An empty field keeps the old name; the rename command takes everything else, trimming included.
        if (text.Length > 0)
        {
            Host.RenameObject(id, text);
        }
    }

    private string AriaSelected(Guid id) => Selection.SelectedId == id ? "true" : "false";

    private void Select(Guid id)
    {
        Selection.Select(Selection.SelectedId == id ? null : id);
    }

    public void Dispose()
    {
        Store.Changed -= OnSceneChanged;
        Selection.Changed -= OnSelectionChanged;
    }
}
