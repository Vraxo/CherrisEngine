using Cherris;
using ImGuiNET;
using System.Numerics;

namespace CherrisEditor.Inspectors;

public class TransformInspector
{
    public void Draw(Transform transform)
    {
        if (!ImGui.BeginTable("TransformTable", 3, ImGuiTableFlags.Resizable)) return;

        ImGui.TableSetupColumn("Property", ImGuiTableColumnFlags.WidthFixed, 80.0f);
        ImGui.TableSetupColumn("Value", ImGuiTableColumnFlags.WidthStretch);
        ImGui.TableSetupColumn("##Reset", ImGuiTableColumnFlags.WidthFixed, 25.0f);

        // Position
        ImGui.TableNextRow();
        ImGui.TableSetColumnIndex(0);
        ImGui.Text("Position");
        ImGui.TableSetColumnIndex(1);
        Vector3 position = transform.Position;
        if (DrawVector3Control("Position", ref position)) transform.Position = position;
        ImGui.TableSetColumnIndex(2);
        if (ImGui.Button("R##Pos")) transform.Position = Vector3.Zero;

        // Rotation
        ImGui.TableNextRow();
        ImGui.TableSetColumnIndex(0);
        ImGui.Text("Rotation");
        ImGui.TableSetColumnIndex(1);
        Vector3 eulerDegrees = EngineMath.ToEulerAngles(transform.Rotation) * (180.0f / System.MathF.PI);
        if (DrawVector3Control("Rotation", ref eulerDegrees))
        {
            Vector3 eulerRadians = eulerDegrees * (System.MathF.PI / 180.0f);
            transform.Rotation = Quaternion.CreateFromYawPitchRoll(eulerRadians.Y, eulerRadians.X, eulerRadians.Z);
        }
        ImGui.TableSetColumnIndex(2);
        if (ImGui.Button("R##Rot")) transform.Rotation = Quaternion.Identity;

        // Scale
        ImGui.TableNextRow();
        ImGui.TableSetColumnIndex(0);
        ImGui.Text("Scale");
        ImGui.TableSetColumnIndex(1);
        Vector3 scale = transform.Scale;
        if (DrawVector3Control("Scale", ref scale)) transform.Scale = scale;
        ImGui.TableSetColumnIndex(2);
        if (ImGui.Button("R##Sca")) transform.Scale = Vector3.One;

        ImGui.EndTable();
    }

    private static bool DrawVector3Control(string label, ref Vector3 values)
    {
        bool valueChanged = false;
        ImGui.PushID(label);
        var style = ImGui.GetStyle();
        float itemWidth = (ImGui.GetContentRegionAvail().X - (style.ItemSpacing.X * 5) - ImGui.CalcTextSize("X").X * 3) / 3.0f;

        ImGui.PushStyleColor(ImGuiCol.Text, new Vector4(0.8f, 0.2f, 0.2f, 1.0f)); ImGui.Text("X"); ImGui.PopStyleColor(); ImGui.SameLine();
        ImGui.PushItemWidth(itemWidth); if (ImGui.DragFloat($"##{label}X", ref values.X, 0.1f)) valueChanged = true; ImGui.PopItemWidth(); ImGui.SameLine();
        ImGui.PushStyleColor(ImGuiCol.Text, new Vector4(0.2f, 0.8f, 0.2f, 1.0f)); ImGui.Text("Y"); ImGui.PopStyleColor(); ImGui.SameLine();
        ImGui.PushItemWidth(itemWidth); if (ImGui.DragFloat($"##{label}Y", ref values.Y, 0.1f)) valueChanged = true; ImGui.PopItemWidth(); ImGui.SameLine();
        ImGui.PushStyleColor(ImGuiCol.Text, new Vector4(0.2f, 0.3f, 0.8f, 1.0f)); ImGui.Text("Z"); ImGui.PopStyleColor(); ImGui.SameLine();
        ImGui.PushItemWidth(itemWidth); if (ImGui.DragFloat($"##{label}Z", ref values.Z, 0.1f)) valueChanged = true; ImGui.PopItemWidth();
        ImGui.PopID();

        return valueChanged;
    }
}