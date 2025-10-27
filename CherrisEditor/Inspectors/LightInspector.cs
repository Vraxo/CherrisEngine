using Cherris;
using CherrisEditor.Undo;
using CherrisEditor.Undo.Commands;
using ImGuiNET;
using System.Numerics;

namespace CherrisEditor.Inspectors;

[CustomInspector(typeof(Light))]
public class LightInspector : IComponentInspector
{
    private readonly HistoryManager _history;
    private readonly EditorTextureManager _textureManager;
    private object _undoInitialValue;

    public LightInspector(EditorTextureManager textureManager, HistoryManager history)
    {
        _textureManager = textureManager;
        _history = history;
    }

    public bool Draw(Component component)
    {
        var light = (Light)component;
        bool dirty = false;

        if (!ImGui.BeginTable("LightTable", 3)) return false;

        ImGui.TableSetupColumn("Property", ImGuiTableColumnFlags.WidthStretch, 0.475f);
        ImGui.TableSetupColumn("Value", ImGuiTableColumnFlags.WidthStretch, 0.475f);
        ImGui.TableSetupColumn("##Reset", ImGuiTableColumnFlags.WidthStretch, 0.05f);

        IntPtr resetIcon = _textureManager.GetTexture("Reset");
        float buttonSize = ImGui.GetFrameHeight() - 4;

        // --- Light Type ---
        ImGui.TableNextRow();
        ImGui.TableSetColumnIndex(0); ImGui.Text("Type");
        ImGui.TableSetColumnIndex(1);
        ImGui.PushItemWidth(-1.0f);
        var typeBeforeEdit = light.Type;
        var enumValues = Enum.GetNames<LightType>();
        int currentIndex = (int)light.Type;
        if (ImGui.Combo("##LightType", ref currentIndex, enumValues, enumValues.Length))
        {
            var newType = (LightType)currentIndex;
            if (newType != typeBeforeEdit)
            {
                _history.Execute(new ChangePropertyCommand(
                    light, typeof(Light).GetProperty(nameof(Light.Type)), typeBeforeEdit, newType));
                dirty = true;
            }
        }
        ImGui.PopItemWidth();

        // --- Common Properties ---
        // Color
        ImGui.TableNextRow();
        ImGui.TableSetColumnIndex(0); ImGui.Text("Color");
        ImGui.TableSetColumnIndex(1);
        ImGui.PushItemWidth(-1.0f);
        var colorBeforeEdit = light.Color;
        var color = colorBeforeEdit;
        if (DefaultInspector.DrawColor3Control("##Color", ref color, out bool colorActivated, out bool colorDeactivated))
        {
            light.Color = color; dirty = true;
        }
        HandleUndo(light, nameof(Light.Color), colorBeforeEdit, colorActivated, colorDeactivated);
        ImGui.PopItemWidth();

        ImGui.TableSetColumnIndex(2);
        if (ImGui.ImageButton("ResetColor", resetIcon, new Vector2(buttonSize, buttonSize)))
        {
            var valueBeforeReset = light.Color;
            if (valueBeforeReset != Vector3.One)
            {
                _history.Execute(new ChangePropertyCommand(
                   light, typeof(Light).GetProperty(nameof(Light.Color)), valueBeforeReset, Vector3.One));
                dirty = true;
            }
        }

        // Intensity
        ImGui.TableNextRow();
        ImGui.TableSetColumnIndex(0); ImGui.Text("Intensity");
        ImGui.TableSetColumnIndex(1);
        ImGui.PushItemWidth(-1.0f);
        var intensityBeforeEdit = light.Intensity;
        var intensity = intensityBeforeEdit;
        if (ImGui.DragFloat("##Intensity", ref intensity, 0.01f, 0.0f, float.MaxValue))
        {
            light.Intensity = intensity; dirty = true;
        }
        HandleUndo(light, nameof(Light.Intensity), intensityBeforeEdit, ImGui.IsItemActivated(), ImGui.IsItemDeactivatedAfterEdit());
        ImGui.PopItemWidth();

        ImGui.TableSetColumnIndex(2);
        if (ImGui.ImageButton("ResetIntensity", resetIcon, new Vector2(buttonSize, buttonSize)))
        {
            var valueBeforeReset = light.Intensity;
            if (valueBeforeReset != 1.0f)
            {
                _history.Execute(new ChangePropertyCommand(
                   light, typeof(Light).GetProperty(nameof(Light.Intensity)), valueBeforeReset, 1.0f));
                dirty = true;
            }
        }


        // --- Type-Specific Properties ---
        switch (light.Type)
        {
            case LightType.Directional:
                DrawDirectionalProperties(light, resetIcon, buttonSize, ref dirty);
                break;
            case LightType.Point:
                DrawPointProperties(light, resetIcon, buttonSize, ref dirty);
                break;
            case LightType.Spot:
                DrawSpotProperties(light, resetIcon, buttonSize, ref dirty);
                break;
        }

        ImGui.EndTable();
        return dirty;
    }

    private void DrawDirectionalProperties(Light light, IntPtr resetIcon, float buttonSize, ref bool dirty)
    {
        // Ambient Strength
        ImGui.TableNextRow();
        ImGui.TableSetColumnIndex(0); ImGui.Text("Ambient Strength");
        ImGui.TableSetColumnIndex(1);
        ImGui.PushItemWidth(-1.0f);
        var ambientBeforeEdit = light.AmbientStrength;
        var ambient = ambientBeforeEdit;
        if (ImGui.DragFloat("##AmbientStrength", ref ambient, 0.01f, 0.0f, 1.0f))
        {
            light.AmbientStrength = ambient; dirty = true;
        }
        HandleUndo(light, nameof(Light.AmbientStrength), ambientBeforeEdit, ImGui.IsItemActivated(), ImGui.IsItemDeactivatedAfterEdit());
        ImGui.PopItemWidth();

        ImGui.TableSetColumnIndex(2);
        if (ImGui.ImageButton("ResetAmbient", resetIcon, new Vector2(buttonSize, buttonSize)))
        {
            var valueBeforeReset = light.AmbientStrength;
            if (Math.Abs(valueBeforeReset - 0.3f) > 0.001f)
            {
                _history.Execute(new ChangePropertyCommand(
                   light, typeof(Light).GetProperty(nameof(Light.AmbientStrength)), valueBeforeReset, 0.3f));
                dirty = true;
            }
        }
    }

    private void DrawPointProperties(Light light, IntPtr resetIcon, float buttonSize, ref bool dirty)
    {
        // Range
        ImGui.TableNextRow();
        ImGui.TableSetColumnIndex(0); ImGui.Text("Range");
        ImGui.TableSetColumnIndex(1);
        ImGui.PushItemWidth(-1.0f);
        var rangeBeforeEdit = light.Range;
        var range = rangeBeforeEdit;
        if (ImGui.DragFloat("##Range", ref range, 0.1f, 0.0f, float.MaxValue))
        {
            light.Range = range; dirty = true;
        }
        HandleUndo(light, nameof(Light.Range), rangeBeforeEdit, ImGui.IsItemActivated(), ImGui.IsItemDeactivatedAfterEdit());
        ImGui.PopItemWidth();

        ImGui.TableSetColumnIndex(2);
        if (ImGui.ImageButton("ResetRange", resetIcon, new Vector2(buttonSize, buttonSize)))
        {
            var valueBeforeReset = light.Range;
            if (Math.Abs(valueBeforeReset - 50.0f) > 0.001f)
            {
                _history.Execute(new ChangePropertyCommand(
                   light, typeof(Light).GetProperty(nameof(Light.Range)), valueBeforeReset, 50.0f));
                dirty = true;
            }
        }
    }

    private void DrawSpotProperties(Light light, IntPtr resetIcon, float buttonSize, ref bool dirty)
    {
        // Range
        DrawPointProperties(light, resetIcon, buttonSize, ref dirty); // Spotlights also have a range

        // Inner Cone Angle
        ImGui.TableNextRow();
        ImGui.TableSetColumnIndex(0); ImGui.Text("Inner Cone Angle");
        ImGui.TableSetColumnIndex(1);
        ImGui.PushItemWidth(-1.0f);
        var innerAngleBeforeEdit = light.InnerConeAngle;
        var innerAngle = innerAngleBeforeEdit;
        if (ImGui.DragFloat("##InnerConeAngle", ref innerAngle, 0.1f, 0.0f, light.OuterConeAngle))
        {
            light.InnerConeAngle = innerAngle; dirty = true;
        }
        HandleUndo(light, nameof(Light.InnerConeAngle), innerAngleBeforeEdit, ImGui.IsItemActivated(), ImGui.IsItemDeactivatedAfterEdit());
        ImGui.PopItemWidth();

        ImGui.TableSetColumnIndex(2);
        if (ImGui.ImageButton("ResetInner", resetIcon, new Vector2(buttonSize, buttonSize)))
        {
            var valueBeforeReset = light.InnerConeAngle;
            if (Math.Abs(valueBeforeReset - 12.5f) > 0.001f)
            {
                _history.Execute(new ChangePropertyCommand(
                   light, typeof(Light).GetProperty(nameof(Light.InnerConeAngle)), valueBeforeReset, 12.5f));
                dirty = true;
            }
        }

        // Outer Cone Angle
        ImGui.TableNextRow();
        ImGui.TableSetColumnIndex(0); ImGui.Text("Outer Cone Angle");
        ImGui.TableSetColumnIndex(1);
        ImGui.PushItemWidth(-1.0f);
        var outerAngleBeforeEdit = light.OuterConeAngle;
        var outerAngle = outerAngleBeforeEdit;
        if (ImGui.DragFloat("##OuterConeAngle", ref outerAngle, 0.1f, light.InnerConeAngle, 89.0f))
        {
            light.OuterConeAngle = outerAngle; dirty = true;
        }
        HandleUndo(light, nameof(Light.OuterConeAngle), outerAngleBeforeEdit, ImGui.IsItemActivated(), ImGui.IsItemDeactivatedAfterEdit());
        ImGui.PopItemWidth();

        ImGui.TableSetColumnIndex(2);
        if (ImGui.ImageButton("ResetOuter", resetIcon, new Vector2(buttonSize, buttonSize)))
        {
            var valueBeforeReset = light.OuterConeAngle;
            if (Math.Abs(valueBeforeReset - 17.5f) > 0.001f)
            {
                _history.Execute(new ChangePropertyCommand(
                   light, typeof(Light).GetProperty(nameof(Light.OuterConeAngle)), valueBeforeReset, 17.5f));
                dirty = true;
            }
        }
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
                // Revert the change so the command can apply it
                property.SetValue(target, _undoInitialValue);
                _history.Execute(new ChangePropertyCommand(target, property, _undoInitialValue, valueAfterEdit));
            }
            _undoInitialValue = null;
        }
    }
}