using CherrisEditor;
using ImGuiNET;
using System.Numerics;

namespace Cherris;

internal class InspectorPanel
{
    private Editor _editor;

    public InspectorPanel(Editor editor)
    {
        _editor = editor;
    }

    public void DrawInspectorPanel()
    {
        ImGui.Begin("Inspector");

        GameObject? selectedObject = _editor.GetSelectedGameObject();

        if (selectedObject is not null)
        {
            ImGui.Text($"Selected: {selectedObject.Name}");
            ImGui.Separator();

            // Transform Component
            if (ImGui.CollapsingHeader("Transform", ImGuiTreeNodeFlags.DefaultOpen))
            {
                Vector3 position = selectedObject.Transform.Position;
                if (ImGui.DragFloat3("Position", ref position, 0.1f))
                {
                    selectedObject.Transform.Position = position;
                }

                // Convert quaternion to Euler angles for editing
                Vector3 eulerDegrees = EngineMath.ToEulerAngles(selectedObject.Transform.Rotation) * (180.0f / MathF.PI);

                if (ImGui.DragFloat3("Rotation", ref eulerDegrees, 1.0f))
                {
                    Vector3 eulerRadians = eulerDegrees * (MathF.PI / 180.0f);
                    selectedObject.Transform.Rotation = Quaternion.CreateFromYawPitchRoll(eulerRadians.Y, eulerRadians.X, eulerRadians.Z);
                }

                Vector3 scale = selectedObject.Transform.Scale;

                if (ImGui.DragFloat3("Scale", ref scale, 0.1f))
                {
                    selectedObject.Transform.Scale = scale;
                }
            }
        }
        else
        {
            ImGui.Text("No object selected.");
        }

        ImGui.End();
    }
}
