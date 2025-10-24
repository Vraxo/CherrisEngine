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

            // --- Position ---
            ImGui.TableNextRow();
            ImGui.TableSetColumnIndex(0);
            ImGui.Text("Position");
            ImGui.TableSetColumnIndex(1);
            Vector3 position = selectedObject.Transform.Position;
            if (DrawVector3Control("Position", ref position))
            {
                selectedObject.Transform.Position = position;
            }

            // --- Rotation ---
            ImGui.TableNextRow();
            ImGui.TableSetColumnIndex(0);
            ImGui.Text("Rotation");
            ImGui.TableSetColumnIndex(1);
            Vector3 eulerDegrees = EngineMath.ToEulerAngles(selectedObject.Transform.Rotation) * (180.0f / MathF.PI);
            if (DrawVector3Control("Rotation", ref eulerDegrees))
            {
                Vector3 eulerRadians = eulerDegrees * (MathF.PI / 180.0f);
                selectedObject.Transform.Rotation = Quaternion.CreateFromYawPitchRoll(eulerRadians.Y, eulerRadians.X, eulerRadians.Z);
            }

            // --- Scale ---
            ImGui.TableNextRow();
            ImGui.TableSetColumnIndex(0);
            ImGui.Text("Scale");
            ImGui.TableSetColumnIndex(1);
            Vector3 scale = selectedObject.Transform.Scale;
            if (DrawVector3Control("Scale", ref scale))
            {
                selectedObject.Transform.Scale = scale;
            }

            ImGui.EndTable();
        }
    }

    /// <summary>
    /// A helper function to draw a styled X, Y, Z vector control that fits perfectly.
    /// </summary>
    private static bool DrawVector3Control(string label, ref Vector3 values)
    {
        bool valueChanged = false;

        ImGui.PushID(label);

        // --- Start of Fix: Robust Width Calculation ---
        var style = ImGui.GetStyle();
        float availableWidth = ImGui.GetContentRegionAvail().X;

        // Calculate the total width of all extra elements (labels and spacing)
        float totalLabelWidth = ImGui.CalcTextSize("X").X + ImGui.CalcTextSize("Y").X + ImGui.CalcTextSize("Z").X;
        float totalSpacingWidth = style.ItemSpacing.X * 5; // There are 5 gaps between the 6 items (L-I-L-I-L-I)

        // The remaining width is divided among the 3 input boxes
        float totalInputWidth = availableWidth - totalLabelWidth - totalSpacingWidth;
        float itemWidth = totalInputWidth / 3.0f;
        // --- End of Fix ---

        // X Component (Red)
        ImGui.PushStyleColor(ImGuiCol.Text, new Vector4(0.8f, 0.2f, 0.2f, 1.0f));
        ImGui.Text("X");
        ImGui.PopStyleColor();

        ImGui.SameLine();
        ImGui.PushItemWidth(itemWidth);
        if (ImGui.DragFloat($"##{label}X", ref values.X, 0.1f)) valueChanged = true;
        ImGui.PopItemWidth();
        ImGui.SameLine();

        // Y Component (Green)
        ImGui.PushStyleColor(ImGuiCol.Text, new Vector4(0.2f, 0.8f, 0.2f, 1.0f));
        ImGui.Text("Y");
        ImGui.PopStyleColor();

        ImGui.SameLine();
        ImGui.PushItemWidth(itemWidth);
        if (ImGui.DragFloat($"##{label}Y", ref values.Y, 0.1f)) valueChanged = true;
        ImGui.PopItemWidth();
        ImGui.SameLine();

        // Z Component (Blue)
        ImGui.PushStyleColor(ImGuiCol.Text, new Vector4(0.2f, 0.3f, 0.8f, 1.0f));
        ImGui.Text("Z");
        ImGui.PopStyleColor();

        ImGui.SameLine();
        ImGui.PushItemWidth(itemWidth);
        if (ImGui.DragFloat($"##{label}Z", ref values.Z, 0.1f)) valueChanged = true;
        ImGui.PopItemWidth();

        ImGui.PopID();

        return valueChanged;
    }
}