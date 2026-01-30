using Cherris.Attributes;
using Cherris.Components;
using Cherris.Rendering;
using CherrisEditor.Undo;
using ImGuiNET;
using System.Numerics;
using System.Reflection;

namespace CherrisEditor.Inspectors;

public sealed class PropertyDrawer
{
    private readonly HistoryManager _history;
    private readonly UndoTracker _undo;
    private readonly ResourceNameSynchronizer _resourceSync;
    private readonly TexturePropertyDrawer _textureDrawer;

    public PropertyDrawer(Editor editor, EditorTextureManager textures, HistoryManager history)
    {
        _history = history;
        _undo = new UndoTracker(history);
        _resourceSync = new ResourceNameSynchronizer(editor);
        _textureDrawer = new TexturePropertyDrawer(editor, textures);
    }

    public bool Draw(Component component, PropertyInfo prop, out bool activated, out bool deactivated)
    {
        activated = false;
        deactivated = false;

        object value = prop.GetValue(component) ?? PropertyDrawingPrimitives.CreateDefault(prop.PropertyType);
        var rangeAttr = prop.GetCustomAttribute<RangeAttribute>();
        var colorAttr = prop.GetCustomAttribute<ColorUsageAttribute>();
        var dragDropAttr = prop.GetCustomAttribute<DragDropTargetAttribute>();

        ImGui.PushID(prop.Name);
        bool changed = false;

        if (prop.PropertyType == typeof(float))
        {
            float v = (float)value;
            float min = rangeAttr?.Min ?? float.MinValue;
            float max = rangeAttr?.Max ?? float.MaxValue;
            float speed = rangeAttr?.Speed ?? 0.01f;

            changed = rangeAttr != null
                ? PropertyDrawingPrimitives.SliderFloat("##val", v, min, max, out v, out activated, out deactivated)
                : PropertyDrawingPrimitives.Float("##val", v, speed, min, max, out v, out activated, out deactivated);

            if (changed)
            {
                prop.SetValue(component, v);
            }
        }
        else if (prop.PropertyType == typeof(int))
        {
            int v = (int)value;
            changed = PropertyDrawingPrimitives.Int("##val", v, out v, out activated, out deactivated);
            if (changed)
            {
                prop.SetValue(component, v);
            }
        }
        else if (prop.PropertyType == typeof(bool))
        {
            bool v = (bool)value;
            changed = PropertyDrawingPrimitives.Bool("##val", v, out v, out activated, out deactivated);
            if (changed)
            {
                prop.SetValue(component, v);
            }
        }
        else if (prop.PropertyType == typeof(string))
        {
            string v = (string)value ?? "";
            string newVal;

            if (dragDropAttr != null)
            {
                changed = PropertyDrawingPrimitives.DragDropString("##val", v, dragDropAttr.PayloadType, out newVal, out activated, out deactivated);
                if (changed)
                {
                    prop.SetValue(component, newVal);
                    _resourceSync.Sync(component, prop.Name, newVal);
                }
            }
            else
            {
                changed = PropertyDrawingPrimitives.String("##val", v, out newVal, out activated, out deactivated);
                if (changed)
                {
                    prop.SetValue(component, newVal);
                }
            }
        }
        else if (prop.PropertyType.IsEnum)
        {
            var v = (Enum)value;
            changed = PropertyDrawingPrimitives.EnumField("##val", v, out var newVal, out activated, out deactivated);
            if (changed)
            {
                prop.SetValue(component, newVal);
            }
        }
        else if (prop.PropertyType == typeof(Vector2))
        {
            var v = (Vector2)value;
            changed = PropertyDrawingPrimitives.Vector2Field("##val", v, out var newVal, out activated, out deactivated);
            if (changed)
            {
                prop.SetValue(component, newVal);
            }
        }
        else if (prop.PropertyType == typeof(Vector3))
        {
            var v = (Vector3)value;
            changed = colorAttr != null
                ? PropertyDrawingPrimitives.Color3("##val", v, out var newVal, out activated, out deactivated)
                : PropertyDrawingPrimitives.Vector3Field("##val", v, out newVal, out activated, out deactivated);
            if (changed)
            {
                prop.SetValue(component, newVal);
            }
        }
        else if (prop.PropertyType == typeof(ITexture))
        {
            changed = _textureDrawer.Draw(component, prop, (ITexture)value, dragDropAttr, out activated, out deactivated);
        }
        else
        {
            ImGui.Text(value.ToString() ?? "null");
        }

        _undo.Track(component, prop.Name, activated, deactivated);
        ImGui.PopID();
        return changed;
    }
}