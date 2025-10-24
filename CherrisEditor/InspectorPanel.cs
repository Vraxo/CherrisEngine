using Cherris;
using ImGuiNET;
using System.Numerics;

namespace CherrisEditor;

internal class InspectorPanel
{
    private readonly Editor _editor;

    public InspectorPanel(Editor editor)
    {
        _editor = editor;
    }

    public void DrawInspectorPanel()
    {
        ImGui.Begin("Details");

        GameObject? selectedObject = _editor.GetSelectedGameObject();

        if (selectedObject is null)
        {
            ImGui.Text("No object selected.");
        }
        else
        {
            DrawSelectObjectProperties(selectedObject);
        }

        ImGui.End();
    }

    private static void DrawSelectObjectProperties(GameObject selectedObject)
    {
        ImGui.Text($"Selected: {selectedObject.Name}");
        ImGui.Separator();

        if (!ImGui.CollapsingHeader("Transform", ImGuiTreeNodeFlags.DefaultOpen))
        {
            return;
        }

        // Use a table for clean alignment
        if (ImGui.BeginTable("TransformTable", 2, ImGuiTableFlags.Resizable))
        {
            ImGui.TableSetupColumn("Property", ImGuiTableColumnFlags.WidthFixed, 70.0f);
            ImGui.TableSetupColumn("Value", ImGuiTableColumnFlags.WidthStretch);

            // Position
            ImGui.TableNextRow();
            ImGui.TableSetColumnIndex(0);
            ImGui.Text("Position");
            ImGui.TableSetColumnIndex(1);
            Vector3 position = selectedObject.Transform.Position;
            if (ImGui.DragFloat3("##Position", ref position, 0.1f))
            {
                selectedObject.Transform.Position = position;
            }

            // Rotation
            ImGui.TableNextRow();
            ImGui.TableSetColumnIndex(0);
            ImGui.Text("Rotation");
            ImGui.TableSetColumnIndex(1);
            Vector3 eulerDegrees = EngineMath.ToEulerAngles(selectedObject.Transform.Rotation) * (180.0f / MathF.PI);
            if (ImGui.DragFloat3("##Rotation", ref eulerDegrees, 1.0f))
            {
                Vector3 eulerRadians = eulerDegrees * (MathF.PI / 180.0f);
                selectedObject.Transform.Rotation = Quaternion.CreateFromYawPitchRoll(eulerRadians.Y, eulerRadians.X, eulerRadians.Z);
            }

            // Scale
            ImGui.TableNextRow();
            ImGui.TableSetColumnIndex(0);
            ImGui.Text("Scale");
            ImGui.TableSetColumnIndex(1);
            Vector3 scale = selectedObject.Transform.Scale;
            if (ImGui.DragFloat3("##Scale", ref scale, 0.1f))
            {
                selectedObject.Transform.Scale = scale;
            }

            ImGui.EndTable();
        }
    }
}