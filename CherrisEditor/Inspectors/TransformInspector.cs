using Cherris;
using ImGuiNET;
using System;
using System.Numerics;
using System.Reflection;
using CherrisEditor.Undo;
using CherrisEditor.Undo.Commands;

namespace CherrisEditor.Inspectors;

public class TransformInspector
{
    private readonly EditorTextureManager _textureManager;
    private readonly HistoryManager _history;

    private Vector3? _undoInitialVec3;
    private string _activeUndoId = "";

    public TransformInspector(EditorTextureManager textureManager, HistoryManager history)
    {
        _textureManager = textureManager;
        _history = history;
    }

    public bool Draw(Transform transform)
    {
        bool dirty = false;
        if (!ImGui.BeginTable("TransformTable", 3)) return false;

        ImGui.TableSetupColumn("Property", ImGuiTableColumnFlags.WidthStretch, 0.475f);
        ImGui.TableSetupColumn("Value", ImGuiTableColumnFlags.WidthStretch, 0.475f);
        ImGui.TableSetupColumn("##Reset", ImGuiTableColumnFlags.WidthStretch, 0.05f);

        IntPtr resetIcon = _textureManager.GetTexture("Reset");
        float buttonSize = ImGui.GetFrameHeight() - 4;

        // Position
        ImGui.TableNextRow();
        ImGui.TableSetColumnIndex(0); ImGui.Text("Position");
        ImGui.TableSetColumnIndex(1);
        Vector3 position = transform.Position;
        if (DrawVector3Control("Position", ref position))
        {
            transform.Position = position;
            dirty = true;
        }
        HandleVectorUndo("Position", transform.Position, transform);
        ImGui.TableSetColumnIndex(2);
        if (ImGui.ImageButton("ResetPos", resetIcon, new Vector2(buttonSize, buttonSize)))
        {
            transform.Position = Vector3.Zero;
            dirty = true;
        }
        HandleVectorUndo("Position", transform.Position, transform, true);

        // Rotation
        ImGui.TableNextRow();
        ImGui.TableSetColumnIndex(0); ImGui.Text("Rotation");
        ImGui.TableSetColumnIndex(1);
        Vector3 eulerDegrees = EngineMath.ToEulerAngles(transform.Rotation) * (180.0f / System.MathF.PI);
        if (DrawVector3Control("Rotation", ref eulerDegrees))
        {
            Vector3 eulerRadians = eulerDegrees * (System.MathF.PI / 180.0f);
            transform.Rotation = Quaternion.CreateFromYawPitchRoll(eulerRadians.Y, eulerRadians.X, eulerRadians.Z);
            dirty = true;
        }
        HandleVectorUndo("Rotation", eulerDegrees, transform);
        ImGui.TableSetColumnIndex(2);
        if (ImGui.ImageButton("ResetRot", resetIcon, new Vector2(buttonSize, buttonSize)))
        {
            transform.Rotation = Quaternion.Identity;
            dirty = true;
        }
        HandleVectorUndo("Rotation", eulerDegrees, transform, true);

        // Scale
        ImGui.TableNextRow();
        ImGui.TableSetColumnIndex(0); ImGui.Text("Scale");
        ImGui.TableSetColumnIndex(1);
        Vector3 scale = transform.Scale;
        if (DrawVector3Control("Scale", ref scale))
        {
            transform.Scale = scale;
            dirty = true;
        }
        HandleVectorUndo("Scale", transform.Scale, transform);
        ImGui.TableSetColumnIndex(2);
        if (ImGui.ImageButton("ResetSca", resetIcon, new Vector2(buttonSize, buttonSize)))
        {
            transform.Scale = Vector3.One;
            dirty = true;
        }
        HandleVectorUndo("Scale", transform.Scale, transform, true);

        ImGui.EndTable();
        return dirty;
    }

    private void HandleVectorUndo(string id, Vector3 currentValue, Transform transform, bool force = false)
    {
        if ((ImGui.IsItemActivated() || force) && _activeUndoId == "")
        {
            _activeUndoId = id;
            _undoInitialVec3 = currentValue;
        }

        if ((ImGui.IsItemDeactivatedAfterEdit() || force) && _activeUndoId == id)
        {
            if (_undoInitialVec3.HasValue && _undoInitialVec3.Value != currentValue)
            {
                var oldValue = _undoInitialVec3.Value;
                PropertyInfo propInfo = typeof(Transform).GetProperty(id);

                ICommand command = null;
                if (id == "Rotation")
                {
                    var oldQuat = Quaternion.CreateFromYawPitchRoll(oldValue.Y * (MathF.PI / 180.0f), oldValue.X * (MathF.PI / 180.0f), oldValue.Z * (MathF.PI / 180.0f));
                    var newQuat = Quaternion.CreateFromYawPitchRoll(currentValue.Y * (MathF.PI / 180.0f), currentValue.X * (MathF.PI / 180.0f), currentValue.Z * (MathF.PI / 180.0f));
                    transform.Rotation = oldQuat; // Revert
                    command = new ChangePropertyCommand(transform, propInfo, oldQuat, newQuat);
                }
                else if (propInfo != null)
                {
                    propInfo.SetValue(transform, oldValue); // Revert
                    command = new ChangePropertyCommand(transform, propInfo, oldValue, currentValue);
                }

                if (command != null)
                {
                    _history.Execute(command);
                }
            }

            _activeUndoId = "";
            _undoInitialVec3 = null;
        }
    }

    private static bool DrawVector3Control(string label, ref Vector3 values)
    {
        bool valueChanged = false;
        ImGui.PushID(label);
        var style = ImGui.GetStyle();
        float itemWidth = (ImGui.GetContentRegionAvail().X - (style.ItemSpacing.X * 5) - ImGui.CalcTextSize("X").X * 3) / 3.0f;

        ImGui.AlignTextToFramePadding();
        ImGui.PushStyleColor(ImGuiCol.Text, new Vector4(0.8f, 0.2f, 0.2f, 1.0f)); ImGui.Text("X"); ImGui.PopStyleColor(); ImGui.SameLine();
        ImGui.PushItemWidth(itemWidth); if (ImGui.DragFloat($"##{label}X", ref values.X, 0.1f)) valueChanged = true; ImGui.PopItemWidth(); ImGui.SameLine();

        ImGui.AlignTextToFramePadding();
        ImGui.PushStyleColor(ImGuiCol.Text, new Vector4(0.2f, 0.8f, 0.2f, 1.0f)); ImGui.Text("Y"); ImGui.PopStyleColor(); ImGui.SameLine();
        ImGui.PushItemWidth(itemWidth); if (ImGui.DragFloat($"##{label}Y", ref values.Y, 0.1f)) valueChanged = true; ImGui.PopItemWidth(); ImGui.SameLine();

        ImGui.AlignTextToFramePadding();
        ImGui.PushStyleColor(ImGuiCol.Text, new Vector4(0.2f, 0.3f, 0.8f, 1.0f)); ImGui.Text("Z"); ImGui.PopStyleColor(); ImGui.SameLine();
        ImGui.PushItemWidth(itemWidth); if (ImGui.DragFloat($"##{label}Z", ref values.Z, 0.1f)) valueChanged = true; ImGui.PopItemWidth();
        ImGui.PopID();

        return valueChanged;
    }
}