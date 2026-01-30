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

        object currentValue = prop.GetValue(component)
            ?? PropertyDrawingPrimitives.CreateDefault(prop.PropertyType);

        bool changed = DispatchDraw(component, prop, currentValue, out activated, out deactivated);

        _undo.Track(component, prop.Name, activated, deactivated);
        return changed;
    }

    private bool DispatchDraw(Component component, PropertyInfo prop, object currentValue, out bool activated, out bool deactivated)
    {
        activated = false;
        deactivated = false;

        return prop.PropertyType switch
        {
            Type t when t == typeof(float) => DrawFloat(component, prop, (float)currentValue, out activated, out deactivated),
            Type t when t == typeof(int) => DrawInt(component, prop, (int)currentValue, out activated, out deactivated),
            Type t when t == typeof(bool) => DrawBool(component, prop, (bool)currentValue, out activated, out deactivated),
            Type t when t == typeof(string) => DrawString(component, prop, (string)currentValue, out activated, out deactivated),
            _ when prop.PropertyType.IsEnum => DrawEnum(component, prop, (Enum)currentValue, out activated, out deactivated),
            Type t when t == typeof(Vector2) => DrawVector2(component, prop, (Vector2)currentValue, out activated, out deactivated),
            Type t when t == typeof(Vector3) => DrawVector3(component, prop, (Vector3)currentValue, out activated, out deactivated),
            Type t when t == typeof(ITexture) => _textureDrawer.Draw(component, prop, (ITexture)currentValue, prop.GetCustomAttribute<DragDropTargetAttribute>(), out activated, out deactivated),
            _ => DrawFallback(currentValue)
        };
    }

    private static bool DrawFloat(Component component, PropertyInfo prop, float currentValue, out bool activated, out bool deactivated)
    {
        ImGui.PushID(prop.Name);
        var rangeAttr = prop.GetCustomAttribute<RangeAttribute>();
        float newValue = currentValue;

        bool changed = rangeAttr != null
            ? PropertyDrawingPrimitives.SliderFloat("##val", currentValue, rangeAttr.Min, rangeAttr.Max, out newValue, out activated, out deactivated)
            : PropertyDrawingPrimitives.Float("##val", currentValue, 0.01f, float.MinValue, float.MaxValue, out newValue, out activated, out deactivated);

        if (changed)
        {
            prop.SetValue(component, newValue);
        }

        ImGui.PopID();
        return changed;
    }

    private static bool DrawInt(Component component, PropertyInfo prop, int currentValue, out bool activated, out bool deactivated)
    {
        ImGui.PushID(prop.Name);
        int newValue = currentValue;

        bool changed = PropertyDrawingPrimitives.Int("##val", currentValue, out newValue, out activated, out deactivated);

        if (changed)
        {
            prop.SetValue(component, newValue);
        }

        ImGui.PopID();
        return changed;
    }

    private static bool DrawBool(Component component, PropertyInfo prop, bool currentValue, out bool activated, out bool deactivated)
    {
        ImGui.PushID(prop.Name);
        bool newValue = currentValue;

        bool changed = PropertyDrawingPrimitives.Bool("##val", currentValue, out newValue, out activated, out deactivated);

        if (changed)
        {
            prop.SetValue(component, newValue);
        }

        ImGui.PopID();
        return changed;
    }

    private bool DrawString(Component component, PropertyInfo prop, string currentValue, out bool activated, out bool deactivated)
    {
        ImGui.PushID(prop.Name);

        var dragDropAttr = prop.GetCustomAttribute<DragDropTargetAttribute>();
        string newValue;
        bool changed;

        if (dragDropAttr != null)
        {
            changed = PropertyDrawingPrimitives.DragDropString("##val", currentValue, dragDropAttr.PayloadType, out newValue, out activated, out deactivated);

            if (changed)
            {
                prop.SetValue(component, newValue);
                _resourceSync.Sync(component, prop.Name, newValue);
            }
        }
        else
        {
            changed = PropertyDrawingPrimitives.String("##val", currentValue, out newValue, out activated, out deactivated);

            if (changed)
            {
                prop.SetValue(component, newValue);
            }
        }

        ImGui.PopID();
        return changed;
    }

    private static bool DrawEnum(Component component, PropertyInfo prop, Enum currentValue, out bool activated, out bool deactivated)
    {
        ImGui.PushID(prop.Name);
        Enum newValue = currentValue;

        bool changed = PropertyDrawingPrimitives.EnumField("##val", currentValue, out newValue, out activated, out deactivated);

        if (changed)
        {
            prop.SetValue(component, newValue);
        }

        ImGui.PopID();
        return changed;
    }

    private static bool DrawVector2(Component component, PropertyInfo prop, Vector2 currentValue, out bool activated, out bool deactivated)
    {
        ImGui.PushID(prop.Name);

        bool changed = PropertyDrawingPrimitives.Vector2Field("##val", currentValue, out Vector2 newValue, out activated, out deactivated);

        if (changed)
        {
            prop.SetValue(component, newValue);
        }

        ImGui.PopID();
        return changed;
    }

    private static bool DrawVector3(Component component, PropertyInfo prop, Vector3 currentValue, out bool activated, out bool deactivated)
    {
        ImGui.PushID(prop.Name);
        var colorAttr = prop.GetCustomAttribute<ColorUsageAttribute>();

        bool changed = colorAttr != null
            ? PropertyDrawingPrimitives.Color3("##val", currentValue, out Vector3 newValue, out activated, out deactivated)
            : PropertyDrawingPrimitives.Vector3Field("##val", currentValue, out newValue, out activated, out deactivated);

        if (changed)
        {
            prop.SetValue(component, newValue);
        }

        ImGui.PopID();
        return changed;
    }

    private static bool DrawFallback(object currentValue)
    {
        ImGui.Text(currentValue.ToString() ?? "null");
        return false;
    }
}