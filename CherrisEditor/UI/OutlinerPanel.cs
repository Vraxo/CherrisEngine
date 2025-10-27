using Cherris;
using ImGuiNET;
using System.Runtime.InteropServices;

namespace CherrisEditor.UI;

public class OutlinerPanel : IDisposable
{
    private readonly Editor _editor;
    private static IntPtr _payloadGuidPtr = IntPtr.Zero; // For GUID payloads

    public OutlinerPanel(Editor editor)
    {
        _editor = editor;
    }

    public void Draw()
    {
        // Free the unmanaged memory from the *previous* frame's drag-drop operation.
        if (_payloadGuidPtr != IntPtr.Zero)
        {
            Marshal.FreeHGlobal(_payloadGuidPtr);
            _payloadGuidPtr = IntPtr.Zero;
        }

        ImGui.Begin("Outliner");
        DrawOutlinerContextMenu();

        foreach (var go in _editor.SceneManager.GameObjects.Where(g => g.Transform.Parent == null).ToList())
        {
            DrawGameObjectNode(go);
        }

        // Use the remaining space in the window as a drop target to instantiate prefabs at the root
        ImGui.InvisibleButton("OutlinerDropTarget", ImGui.GetContentRegionAvail());
        if (ImGui.BeginDragDropTarget())
        {
            ImGuiPayloadPtr prefabPayload = ImGui.AcceptDragDropPayload("ASSET_PATH_PREFAB");
            if (prefabPayload.Data != IntPtr.Zero)
            {
                string path = Marshal.PtrToStringAnsi(prefabPayload.Data);
                _editor.InstantiatePrefab(path);
            }

            ImGuiPayloadPtr goPayload = ImGui.AcceptDragDropPayload("GAMEOBJECT_ID");
            if (goPayload.Data != IntPtr.Zero)
            {
                byte[] data = new byte[goPayload.DataSize];
                Marshal.Copy(goPayload.Data, data, 0, goPayload.DataSize);
                var draggedId = new Guid(data);
                var draggedObject = _editor.SceneManager.GameObjects.FirstOrDefault(g => g.Id == draggedId);
                if (draggedObject != null)
                {
                    draggedObject.Transform.Parent = null; // Unparent
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
            // Allocate memory and hold onto the pointer until the next frame.
            _payloadGuidPtr = Marshal.AllocHGlobal(guidBytes.Length);
            Marshal.Copy(guidBytes, 0, _payloadGuidPtr, guidBytes.Length);
            ImGui.SetDragDropPayload("GAMEOBJECT_ID", _payloadGuidPtr, (uint)guidBytes.Length);

            ImGui.Text(go.Name);
            ImGui.EndDragDropSource();
        }

        if (ImGui.BeginDragDropTarget())
        {
            ImGuiPayloadPtr payload = ImGui.AcceptDragDropPayload("GAMEOBJECT_ID");
            if (payload.Data != IntPtr.Zero)
            {
                byte[] data = new byte[payload.DataSize];
                Marshal.Copy(payload.Data, data, 0, payload.DataSize);
                var draggedId = new Guid(data);
                GameObject draggedObject = _editor.SceneManager.GameObjects.First(g => g.Id == draggedId);
                draggedObject.Transform.Parent = go.Transform;
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

    private void DrawOutlinerContextMenu()
    {
        if (!ImGui.BeginPopupContextWindow("OutlinerContextMenu"))
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
        // Ensure we free the handle on shutdown if it's still allocated
        if (_payloadGuidPtr != IntPtr.Zero)
        {
            Marshal.FreeHGlobal(_payloadGuidPtr);
            _payloadGuidPtr = IntPtr.Zero;
        }
    }
}