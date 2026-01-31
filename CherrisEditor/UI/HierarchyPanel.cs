using Cherris.Core;
using ImGuiNET;
using System.Runtime.InteropServices;
using System.Linq;

namespace CherrisEditor.UI;

public class HierarchyPanel : IDisposable
{
    private readonly Editor _editor;
    private static IntPtr _payloadGuidPtr = IntPtr.Zero;
    private string _searchQuery = "";

    public HierarchyPanel(Editor editor)
    {
        _editor = editor;
    }

    public void Draw()
    {
        ImGui.Begin("Hierarchy");

        ImGui.InputTextWithHint("##HierarchySearch", "Search...", ref _searchQuery, 256);
        ImGui.Separator();

        DrawHierarchyContextMenu();

        foreach (var go in _editor.SceneManager.GameObjects.Where(g => g.Transform.Parent is null).ToList())
        {
            DrawGameObjectNode(go);
        }

        ImGui.InvisibleButton("HierarchyDropTarget", ImGui.GetContentRegionAvail());
        if (ImGui.BeginDragDropTarget())
        {
            AcceptPrefabDrop();
            AcceptGameObjectDrop();
            ImGui.EndDragDropTarget();
        }

        ImGui.End();

        CleanupPayloadMemory();
    }

    private bool ShouldShowGameObject(GameObject go)
    {
        if (string.IsNullOrWhiteSpace(_searchQuery))
            return true;

        if (go.Name.Contains(_searchQuery, StringComparison.OrdinalIgnoreCase))
            return true;

        return go.Transform.Children.Any(child => ShouldShowGameObject(child.GameObject));
    }

    private void DrawGameObjectNode(GameObject go)
    {
        if (!ShouldShowGameObject(go))
            return;

        ImGuiTreeNodeFlags flags = ImGuiTreeNodeFlags.OpenOnArrow | ImGuiTreeNodeFlags.SpanAvailWidth;
        if (_editor.GetSelectedGameObject() == go)
            flags |= ImGuiTreeNodeFlags.Selected;
        if (go.Transform.Children.Count == 0)
            flags |= ImGuiTreeNodeFlags.Leaf;

        if (!string.IsNullOrWhiteSpace(_searchQuery))
            ImGui.SetNextItemOpen(true, ImGuiCond.Always);

        bool nodeOpen = ImGui.TreeNodeEx(go.Id.ToString(), flags, go.Name);

        if (ImGui.IsItemClicked())
            _editor.SetSelectedGameObject(go);

        if (ImGui.BeginDragDropSource())
        {
            PrepareDragPayload(go);
            ImGui.Text(go.Name);
            ImGui.EndDragDropSource();
        }

        if (ImGui.BeginDragDropTarget())
        {
            AcceptChildDrop(go);
            ImGui.EndDragDropTarget();
        }

        if (nodeOpen)
        {
            foreach (var child in go.Transform.Children.ToList())
            {
                DrawGameObjectNode(child.GameObject);
            }
            ImGui.TreePop();
        }
    }

    private void PrepareDragPayload(GameObject go)
    {
        byte[] guidBytes = go.Id.ToByteArray();
        _payloadGuidPtr = Marshal.AllocHGlobal(guidBytes.Length);
        Marshal.Copy(guidBytes, 0, _payloadGuidPtr, guidBytes.Length);
        ImGui.SetDragDropPayload(EditorConstants.DragDropPayloads.GameObjectId, _payloadGuidPtr, (uint)guidBytes.Length);
    }

    private void CleanupPayloadMemory()
    {
        if (_payloadGuidPtr != IntPtr.Zero)
        {
            Marshal.FreeHGlobal(_payloadGuidPtr);
            _payloadGuidPtr = IntPtr.Zero;
        }
    }

    private unsafe void AcceptPrefabDrop()
    {
        var payload = ImGui.AcceptDragDropPayload(EditorConstants.DragDropPayloads.Prefab);
        if (payload.NativePtr == null)
            return;

        string? path = Marshal.PtrToStringAnsi(payload.Data);
        if (!string.IsNullOrEmpty(path))
            _editor.SceneOperations.InstantiatePrefab(path);
    }

    private unsafe void AcceptGameObjectDrop()
    {
        var payload = ImGui.AcceptDragDropPayload(EditorConstants.DragDropPayloads.GameObjectId);
        if (payload.NativePtr == null || payload.DataSize != 16)
            return;

        byte[] data = new byte[16];
        Marshal.Copy(payload.Data, data, 0, 16);
        Guid draggedId = new(data);

        GameObject? dragged = _editor.SceneManager.GameObjects.FirstOrDefault(g => g.Id == draggedId);
        if (dragged != null)
            dragged.Transform.Parent = null;
    }

    private unsafe void AcceptChildDrop(GameObject parent)
    {
        var payload = ImGui.AcceptDragDropPayload(EditorConstants.DragDropPayloads.GameObjectId);
        if (payload.NativePtr == null || payload.DataSize != 16)
            return;

        byte[] data = new byte[16];
        Marshal.Copy(payload.Data, data, 0, 16);
        Guid draggedId = new(data);

        GameObject? dragged = _editor.SceneManager.GameObjects.FirstOrDefault(g => g.Id == draggedId);
        if (dragged != null && dragged != parent)
            dragged.Transform.Parent = parent.Transform;
    }

    private void DrawHierarchyContextMenu()
    {
        if (!ImGui.BeginPopupContextWindow("HierarchyContextMenu"))
            return;

        if (ImGui.MenuItem("Create Empty GameObject"))
        {
            GameObject newGo = new("New GameObject");
            _editor.SceneManager.AddGameObject(newGo);
            _editor.SetSelectedGameObject(newGo);
        }

        if (ImGui.MenuItem("Delete", "Del") && _editor.GetSelectedGameObject() is not null)
        {
            _editor.SceneManager.RemoveGameObject(_editor.GetSelectedGameObject());
            _editor.SetSelectedGameObject(null);
        }

        ImGui.EndPopup();
    }

    public void Dispose()
    {
        CleanupPayloadMemory();
    }
}