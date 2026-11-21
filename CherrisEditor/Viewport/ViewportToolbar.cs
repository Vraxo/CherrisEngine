using ImGuiNET;
using System.Numerics;

namespace CherrisEditor.UI.Viewport;

public class ViewportToolbar
{
    private readonly Editor _editor;

    public ViewportToolbar(Editor editor)
    {
        _editor = editor;
    }

    public void Draw()
    {
        ImGui.BeginChild("ViewportToolbar", new Vector2(0, ImGui.GetFrameHeightWithSpacing()), false,
            ImGuiWindowFlags.NoScrollbar | ImGuiWindowFlags.NoScrollWithMouse);

        ImGui.PushStyleVar(ImGuiStyleVar.FramePadding, new Vector2(4, 4));
        DrawOptionsDropdown();
        ImGui.PopStyleVar();

        ImGui.EndChild();
    }

    private void DrawOptionsDropdown()
    {
        ImGui.PushStyleColor(ImGuiCol.Button, new Vector4(0.2f, 0.2f, 0.2f, 0.8f));
        ImGui.PushStyleColor(ImGuiCol.ButtonHovered, new Vector4(0.3f, 0.3f, 0.3f, 0.9f));
        ImGui.PushStyleColor(ImGuiCol.ButtonActive, new Vector4(0.25f, 0.25f, 0.25f, 0.9f));

        if (ImGui.Button("Options"))
        {
            ImGui.OpenPopup("ViewportOptionsPopup");
        }

        if (ImGui.BeginPopup("ViewportOptionsPopup"))
        {
            DrawOptionsMenu();
            ImGui.EndPopup();
        }

        ImGui.PopStyleColor(3);
    }

    private void DrawOptionsMenu()
    {
        bool showGrid = _editor.Renderer.ShowGrid;
        if (ImGui.Checkbox("Show Grid", ref showGrid))
        {
            _editor.Renderer.ShowGrid = showGrid;
        }
    }
}