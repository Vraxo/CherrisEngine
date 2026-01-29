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

        if (PropertyDrawer.Vector3($"##{propName}", ref value, out bool activated, out bool deactivated))
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

        if (PropertyDrawer.Vector3("##Rotation", ref eulerDegrees, out bool activated, out bool deactivated))
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