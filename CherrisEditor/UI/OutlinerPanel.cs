using Cherris;
using ImGuiNET;
using System;
using System.Linq;
using System.Runtime.InteropServices;

namespace CherrisEditor.UI;

public class OutlinerPanel
{
    private readonly Editor _editor;

    public OutlinerPanel(Editor editor)
    {
        _editor = editor;
    }

    public unsafe void Draw()
    {
        ImGui.Begin("Outliner");
        DrawOutlinerContextMenu();

        foreach (var go in _editor.SceneManager.GameObjects.Where(g => g.Transform.Parent == null).ToList())
        {
            DrawGameObjectNode(go);
        }

        ImGui.End();
    }

    private unsafe void DrawGameObjectNode(GameObject go)
    {
        ImGuiTreeNodeFlags flags = ImGuiTreeNodeFlags.OpenOnArrow | ImGuiTreeNodeFlags.SpanAvailWidth;
        if (_editor.GetSelectedGameObject() == go) flags |= ImGuiTreeNodeFlags.Selected;
        if (go.Transform.Children.Count == 0) flags |= ImGuiTreeNodeFlags.Leaf;

        bool nodeOpen = ImGui.TreeNodeEx(go.Id.ToString(), flags, go.Name);

        if (ImGui.IsItemClicked()) _editor.SetSelectedGameObject(go);

        if (ImGui.BeginDragDropSource())
        {
            byte[] guidBytes = go.Id.ToByteArray();
            fixed (byte* ptr = guidBytes)
            {
                ImGui.SetDragDropPayload("GAMEOBJECT_ID", (IntPtr)ptr, (uint)guidBytes.Length);
            }
            ImGui.Text(go.Name);
            ImGui.EndDragDropSource();
        }

        if (ImGui.BeginDragDropTarget())
        {
            ImGuiPayloadPtr payload = ImGui.AcceptDragDropPayload("GAMEOBJECT_ID");
            if (payload.NativePtr is not null)
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
}