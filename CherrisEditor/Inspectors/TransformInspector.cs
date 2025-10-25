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
    private object _undoInitialValue;

    public TransformInspector(EditorTextureManager textureManager, HistoryManager history)
    {
        _textureManager = textureManager;
        _history = history;
    }

    public bool Draw(Transform transform)
    {
        bool dirty = false;
        if (!ImGui.BeginTable("TransformTable", 3)) return false;

        ImGui.TableSetupColumn("Property", ImGuiTableColumnFlags.WidthStretch, 0.25f);
        ImGui.TableSetupColumn("Value", ImGuiTableColumnFlags.WidthStretch, 0.70f);
        ImGui.TableSetupColumn("##Reset", ImGuiTableColumnFlags.WidthStretch, 0.05f);

        IntPtr resetIcon = _textureManager.GetTexture("Reset");
        float buttonSize = ImGui.GetFrameHeight() - 4;

        // Position
        ImGui.TableNextRow();
        ImGui.TableSetColumnIndex(0); ImGui.Text("Position");
        ImGui.TableSetColumnIndex(1);
        var posBeforeEdit = transform.Position;
        var position = posBeforeEdit;
        if (DefaultInspector.DrawVector3Control("Position", ref position, out bool posActivated, out bool posDeactivated))
        {
            transform.Position = position;
            dirty = true;
        }
        HandleUndo(transform, nameof(Transform.Position), posBeforeEdit, posActivated, posDeactivated);

        ImGui.TableSetColumnIndex(2);
        if (ImGui.ImageButton("ResetPos", resetIcon, new Vector2(buttonSize, buttonSize)))
        {
            var valueBeforeReset = transform.Position;
            if (valueBeforeReset != Vector3.Zero)
            {
                transform.Position = Vector3.Zero;
                _history.Execute(new ChangePropertyCommand(transform, typeof(Transform).GetProperty(nameof(Transform.Position)), valueBeforeReset, Vector3.Zero));
                dirty = true;
            }
        }

        // Rotation
        ImGui.TableNextRow();
        ImGui.TableSetColumnIndex(0); ImGui.Text("Rotation");
        ImGui.TableSetColumnIndex(1);
        var rotBeforeEdit = transform.Rotation;
        Vector3 eulerDegrees = EngineMath.ToEulerAngles(rotBeforeEdit) * (180.0f / System.MathF.PI);
        if (DefaultInspector.DrawVector3Control("Rotation", ref eulerDegrees, out bool rotActivated, out bool rotDeactivated))
        {
            Vector3 eulerRadians = eulerDegrees * (System.MathF.PI / 180.0f);
            transform.Rotation = Quaternion.CreateFromYawPitchRoll(eulerRadians.Y, eulerRadians.X, eulerRadians.Z);
            dirty = true;
        }
        HandleUndo(transform, nameof(Transform.Rotation), rotBeforeEdit, rotActivated, rotDeactivated);


        ImGui.TableSetColumnIndex(2);
        if (ImGui.ImageButton("ResetRot", resetIcon, new Vector2(buttonSize, buttonSize)))
        {
            var valueBeforeReset = transform.Rotation;
            if (valueBeforeReset != Quaternion.Identity)
            {
                transform.Rotation = Quaternion.Identity;
                _history.Execute(new ChangePropertyCommand(transform, typeof(Transform).GetProperty(nameof(Transform.Rotation)), valueBeforeReset, Quaternion.Identity));
                dirty = true;
            }
        }

        // Scale
        ImGui.TableNextRow();
        ImGui.TableSetColumnIndex(0); ImGui.Text("Scale");
        ImGui.TableSetColumnIndex(1);
        var scaleBeforeEdit = transform.Scale;
        var scale = scaleBeforeEdit;
        if (DefaultInspector.DrawVector3Control("Scale", ref scale, out bool scaleActivated, out bool scaleDeactivated))
        {
            transform.Scale = scale;
            dirty = true;
        }
        HandleUndo(transform, nameof(Transform.Scale), scaleBeforeEdit, scaleActivated, scaleDeactivated);


        ImGui.TableSetColumnIndex(2);
        if (ImGui.ImageButton("ResetSca", resetIcon, new Vector2(buttonSize, buttonSize)))
        {
            var valueBeforeReset = transform.Scale;
            if (valueBeforeReset != Vector3.One)
            {
                transform.Scale = Vector3.One;
                _history.Execute(new ChangePropertyCommand(transform, typeof(Transform).GetProperty(nameof(Transform.Scale)), valueBeforeReset, Vector3.One));
                dirty = true;
            }
        }

        ImGui.EndTable();
        return dirty;
    }

    private void HandleUndo(object target, string propertyName, object valueBeforeEdit, bool activated, bool deactivated)
    {
        var property = target.GetType().GetProperty(propertyName);
        if (property == null) return;

        if (activated)
        {
            _undoInitialValue = valueBeforeEdit;
        }

        if (deactivated)
        {
            object valueAfterEdit = property.GetValue(target);
            if (_undoInitialValue != null && !_undoInitialValue.Equals(valueAfterEdit))
            {
                property.SetValue(target, _undoInitialValue);
                _history.Execute(new ChangePropertyCommand(target, property, _undoInitialValue, valueAfterEdit));
            }
            _undoInitialValue = null;
        }
    }
}