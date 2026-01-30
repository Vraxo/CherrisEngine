using Cherris.Core;
using ImGuiNET;
using System.Runtime.InteropServices;

namespace CherrisEditor.UI;

public class HierarchyPanel : IDisposable
{
    private readonly Editor _editor;
    private static IntPtr _payloadGuidPtr = IntPtr.Zero;

    public HierarchyPanel(Editor editor)
    {
        _editor = editor;
    }

    public void Draw()
    {
        if (_payloadGuidPtr != IntPtr.Zero)
        {
            Marshal.FreeHGlobal(_payloadGuidPtr);
            _payloadGuidPtr = IntPtr.Zero;
        }

        ImGui.Begin("Hierarchy");
        DrawHierarchyContextMenu();

        foreach (var go in _editor.SceneManager.GameObjects.Where(g => g.Transform.Parent is null).ToList())
        {
            DrawGameObjectNode(go);
        }

        ImGui.InvisibleButton("HierarchyDropTarget", ImGui.GetContentRegionAvail());
        if (ImGui.BeginDragDropTarget())
        {
            ImGuiPayloadPtr prefabPayload = ImGui.AcceptDragDropPayload(EditorConstants.DragDropPayloads.Prefab);
            unsafe
            {
                if (prefabPayload.NativePtr != null)
                {
                    string path = Marshal.PtrToStringAnsi(prefabPayload.Data);
                    _editor.SceneOperations.InstantiatePrefab(path);
                }
            }

            ImGuiPayloadPtr goPayload = ImGui.AcceptDragDropPayload(EditorConstants.DragDropPayloads.GameObjectId);
            unsafe
            {
                if (goPayload.NativePtr != null)
                {
                    byte[] data = new byte[goPayload.DataSize];
                    Marshal.Copy(goPayload.Data, data, 0, goPayload.DataSize);
                    var draggedId = new Guid(data);
                    var draggedObject = _editor.SceneManager.GameObjects.FirstOrDefault(g => g.Id == draggedId);
                    if (draggedObject is not null)
                    {
                        draggedObject.Transform.Parent = null;
                    }
                }
            }

            ImGui.EndDragDropTarget();
        }

        ImGui.End();
    }

    private void DrawGameObjectNode(GameObject go)
    {
        ImGuiTreeNodeFlags flags = ImGuiTreeNodeFlags.OpenOnArrow | ImGuiTreeNodeFlags.SpanAvailWidth;
        if (_editor.GetSelectedGameObject() == go) flags |= ImGuiTreeNodeFlags.Selected;
        if (go.Transform.Children.Count == 0) flags |= ImGuiTreeNodeFlags.Leaf;

        bool nodeOpen = ImGui.TreeNodeEx(go.Id.ToString(), flags, go.Name);

        if (ImGui.IsItemClicked()) _editor.SetSelectedGameObject(go);

        if (ImGui.BeginDragDropSource())
        {
            byte[] guidBytes = go.Id.ToByteArray();
            _payloadGuidPtr = Marshal.AllocHGlobal(guidBytes.Length);
            Marshal.Copy(guidBytes, 0, _payloadGuidPtr, guidBytes.Length);
            ImGui.SetDragDropPayload(EditorConstants.DragDropPayloads.GameObjectId, _payloadGuidPtr, (uint)guidBytes.Length);

            ImGui.Text(go.Name);
            ImGui.EndDragDropSource();
        }

        if (ImGui.BeginDragDropTarget())
        {
            ImGuiPayloadPtr payload = ImGui.AcceptDragDropPayload(EditorConstants.DragDropPayloads.GameObjectId);
            unsafe
            {
                if (payload.NativePtr != null)
                {
                    byte[] data = new byte[payload.DataSize];
                    Marshal.Copy(payload.Data, data, 0, payload.DataSize);
                    var draggedId = new Guid(data);
                    GameObject draggedObject = _editor.SceneManager.GameObjects.First(g => g.Id == draggedId);
                    draggedObject.Transform.Parent = go.Transform;
                }
            }
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

    private void DrawHierarchyContextMenu()
    {
        if (!ImGui.BeginPopupContextWindow("HierarchyContextMenu"))
        {
            return;
        }

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
        if (_payloadGuidPtr == IntPtr.Zero)
        {
            return;
        }

        Marshal.FreeHGlobal(_payloadGuidPtr);
        _payloadGuidPtr = IntPtr.Zero;
    }
}