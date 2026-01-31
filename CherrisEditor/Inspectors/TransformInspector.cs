using Cherris;
using Cherris.Components;
using CherrisEditor.Undo;
using CherrisEditor.Undo.Commands;
using ImGuiNET;
using System.Numerics;
using System.Reflection;

namespace CherrisEditor.Inspectors;

public sealed class TransformInspector
{
    private readonly EditorTextureManager _textures;
    private readonly HistoryManager _history;
    private readonly UndoTracker _undo;

    public TransformInspector(EditorTextureManager textures, HistoryManager history)
    {
        _textures = textures;
        _history = history;
        _undo = new UndoTracker(history);
    }

    public bool Draw(Transform transform)
    {
        bool dirty = false;

        if (!ImGui.BeginTable("TransformTable", 3))
        {
            return false;
        }

        ImGui.TableSetupColumn("Property", ImGuiTableColumnFlags.WidthStretch, 0.25f);
        ImGui.TableSetupColumn("Value", ImGuiTableColumnFlags.WidthStretch, 0.70f);
        ImGui.TableSetupColumn("##Reset", ImGuiTableColumnFlags.WidthStretch, 0.05f);

        DrawVector3Property(transform, nameof(Transform.Position), Vector3.Zero, ref dirty);
        DrawRotationProperty(transform, ref dirty);
        DrawVector3Property(transform, nameof(Transform.Scale), Vector3.One, ref dirty);

        ImGui.EndTable();
        return dirty;
    }

    private void DrawVector3Property(Transform target, string propName, Vector3 defaultValue, ref bool dirty)
    {
        ImGui.TableNextRow();
        ImGui.TableSetColumnIndex(0);
        ImGui.Text(propName);

        ImGui.TableSetColumnIndex(1);
        ImGui.PushItemWidth(-1);

        var property = typeof(Transform).GetProperty(propName)!;
        var value = (Vector3)property.GetValue(target)!;

        if (DrawVector3Control($"##{propName}", ref value, out bool activated, out bool deactivated))
        {
            property.SetValue(target, value);
            dirty = true;
        }
        _undo.Track(target, propName, activated, deactivated);

        ImGui.PopItemWidth();
        DrawResetButton(target, property, value, defaultValue, ref dirty);
    }

    private void DrawRotationProperty(Transform target, ref bool dirty)
    {
        ImGui.TableNextRow();
        ImGui.TableSetColumnIndex(0);
        ImGui.Text("Rotation");

        ImGui.TableSetColumnIndex(1);
        ImGui.PushItemWidth(-1);

        var currentRot = target.Rotation;
        var eulerDegrees = EngineMath.ToEulerAngles(currentRot) * (180f / MathF.PI);

        if (DrawVector3Control("##Rotation", ref eulerDegrees, out bool activated, out bool deactivated))
        {
            var eulerRadians = eulerDegrees * (MathF.PI / 180f);
            target.Rotation = Quaternion.CreateFromYawPitchRoll(eulerRadians.Y, eulerRadians.X, eulerRadians.Z);
            dirty = true;
        }
        _undo.Track(target, nameof(Transform.Rotation), activated, deactivated);

        ImGui.PopItemWidth();

        var property = typeof(Transform).GetProperty(nameof(Transform.Rotation))!;
        DrawResetButton(target, property, currentRot, Quaternion.Identity, ref dirty);
    }

    private static bool DrawVector3Control(string id, ref Vector3 value, out bool activated, out bool deactivated)
    {
        activated = false;
        deactivated = false;

        ImGui.PushID(id);
        var style = ImGui.GetStyle();
        float itemWidth = (ImGui.GetContentRegionAvail().X - (style.ItemSpacing.X * 5) - (ImGui.CalcTextSize("X").X * 3)) / 3f;
        bool changed = false;
        float[] components = { value.X, value.Y, value.Z };
        string[] labels = { "X", "Y", "Z" };
        Vector4[] colors =
        [
            new(0.8f, 0.2f, 0.2f, 1),
            new(0.2f, 0.8f, 0.2f, 1),
            new(0.2f, 0.3f, 0.8f, 1)
        ];

        for (int i = 0; i < 3; i++)
        {
            if (i > 0)
            {
                ImGui.SameLine();
            }

            ImGui.AlignTextToFramePadding();
            ImGui.PushStyleColor(ImGuiCol.Text, colors[i]);
            ImGui.Text(labels[i]);
            ImGui.PopStyleColor();
            ImGui.SameLine();
            ImGui.PushItemWidth(itemWidth);

            if (ImGui.DragFloat($"##c{i}", ref components[i], 0.1f))
            {
                changed = true;
            }

            activated |= ImGui.IsItemActivated();
            deactivated |= ImGui.IsItemDeactivatedAfterEdit();
            ImGui.PopItemWidth();
        }

        value = new Vector3(components[0], components[1], components[2]);
        ImGui.PopID();
        return changed;
    }

    private void DrawResetButton(Transform target, PropertyInfo property, object current, object defaultValue, ref bool dirty)
    {
        ImGui.TableSetColumnIndex(2);

        IntPtr icon = _textures.GetTexture("Reset");
        float size = ImGui.GetFrameHeight() - 4;

        if (!ImGui.ImageButton($"Reset{property.Name}", icon, new Vector2(size, size)))
        {
            return;
        }

        if (Equals(current, defaultValue))
        {
            return;
        }

        _history.Execute(new ChangePropertyCommand(target, property, current, defaultValue));
        property.SetValue(target, defaultValue);
        dirty = true;
    }
}