using Cherris;
using Cherris.Components;
using CherrisEditor.Undo;
using ImGuiNET;
using System.Numerics;
using System.Reflection;
using System.Text.RegularExpressions;

namespace CherrisEditor.Inspectors;

public sealed class DefaultInspector : IComponentInspector
{
    private readonly EditorTextureManager _textures;
    private readonly HistoryManager _history;
    private readonly UndoTracker _undo;

    public DefaultInspector(EditorTextureManager textures, HistoryManager history)
    {
        _textures = textures;
        _history = history;
        _undo = new UndoTracker(history);
    }

    public bool Draw(Component component)
    {
        bool dirty = false;
        Type type = component.GetType();

        if (!ImGui.BeginTable($"{type.Name}Table", 3))
        {
            return false;
        }

        ImGui.TableSetupColumn("Property", ImGuiTableColumnFlags.WidthStretch, 0.475f);
        ImGui.TableSetupColumn("Value", ImGuiTableColumnFlags.WidthStretch, 0.475f);
        ImGui.TableSetupColumn("##Reset", ImGuiTableColumnFlags.WidthStretch, 0.05f);

        var properties = type.GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Where(p => p.CanRead && p.CanWrite && !p.IsDefined(typeof(HideInInspectorAttribute), false));

        foreach (var prop in properties)
        {
            if (DrawPropertyRow(component, prop))
            {
                dirty = true;
            }
        }

        ImGui.EndTable();
        return dirty;
    }

    private bool DrawPropertyRow(Component component, PropertyInfo prop)
    {
        ImGui.TableNextRow();

        ImGui.TableSetColumnIndex(0);
        ImGui.Text(SplitPascalCase(prop.Name));

        ImGui.TableSetColumnIndex(1);
        ImGui.PushItemWidth(-1);

        bool changed = DrawPropertyControl(component, prop, out bool activated, out bool deactivated);

        _undo.Track(component, prop.Name, activated, deactivated);

        ImGui.PopItemWidth();

        ImGui.TableSetColumnIndex(2);
        if (DrawResetButton(component, prop))
        {
            changed = true;
        }

        return changed;
    }

    private bool DrawPropertyControl(Component component, PropertyInfo prop, out bool activated, out bool deactivated)
    {
        activated = false;
        deactivated = false;

        object value = prop.GetValue(component) ?? CreateDefault(prop.PropertyType);
        bool changed = false;

        ImGui.PushID(prop.Name);

        if (prop.PropertyType == typeof(float))
        {
            float v = (float)value;
            if (PropertyDrawer.Float($"##{prop.Name}", ref v))
            {
                prop.SetValue(component, v);
                changed = true;
            }
        }
        else if (prop.PropertyType == typeof(int))
        {
            int v = (int)value;
            if (PropertyDrawer.Int($"##{prop.Name}", ref v))
            {
                prop.SetValue(component, v);
                changed = true;
            }
        }
        else if (prop.PropertyType == typeof(bool))
        {
            bool v = (bool)value;
            if (PropertyDrawer.Bool($"##{prop.Name}", ref v))
            {
                prop.SetValue(component, v);
                changed = true;
            }
        }
        else if (prop.PropertyType == typeof(string))
        {
            string v = (string)value ?? "";
            if (PropertyDrawer.String($"##{prop.Name}", ref v))
            {
                prop.SetValue(component, v);
                changed = true;
            }
        }
        else if (prop.PropertyType.IsEnum)
        {
            var enumValue = (Enum)value;
            if (DrawEnum(prop.Name, enumValue, out var newValue))
            {
                prop.SetValue(component, newValue);
                changed = true;
            }
        }
        else if (prop.PropertyType == typeof(Vector2))
        {
            var v = (Vector2)value;
            if (PropertyDrawer.Vector2($"##{prop.Name}", ref v, out activated, out deactivated))
            {
                prop.SetValue(component, v);
                changed = true;
            }
        }
        else if (prop.PropertyType == typeof(Vector3))
        {
            var v = (Vector3)value;
            bool isColor = prop.Name.Contains("Color", StringComparison.OrdinalIgnoreCase);

            if (isColor)
            {
                if (PropertyDrawer.Color3($"##{prop.Name}", ref v, out activated, out deactivated))
                {
                    prop.SetValue(component, v);
                    changed = true;
                }
            }
            else
            {
                if (PropertyDrawer.Vector3($"##{prop.Name}", ref v, out activated, out deactivated))
                {
                    prop.SetValue(component, v);
                    changed = true;
                }
            }
        }
        else if (prop.PropertyType == typeof(Vector4))
        {
            var v = (Vector4)value;
            if (ImGui.DragFloat4($"##{prop.Name}", ref v, 0.1f))
            {
                prop.SetValue(component, v);
                changed = true;
            }
        }
        else
        {
            ImGui.Text(value.ToString() ?? "null");
        }

        if (!activated)
        {
            activated = ImGui.IsItemActivated();
        }

        if (!deactivated)
        {
            deactivated = ImGui.IsItemDeactivatedAfterEdit();
        }

        ImGui.PopID();
        return changed;
    }

    private static bool DrawEnum(string id, Enum current, out Enum result)
    {
        result = current;
        var names = Enum.GetNames(current.GetType());
        string currentName = current.ToString();
        int index = Array.IndexOf(names, currentName);

        if (!ImGui.Combo($"##{id}", ref index, names, names.Length))
        {
            return false;
        }

        result = (Enum)Enum.Parse(current.GetType(), names[index]);
        return true;
    }

    private bool DrawResetButton(Component component, PropertyInfo prop)
    {
        IntPtr icon = _textures.GetTexture("Reset");
        float size = ImGui.GetFrameHeight() - 4;

        if (!ImGui.ImageButton($"Reset{prop.Name}", icon, new Vector2(size, size)))
        {
            return false;
        }

        object? defaultValue = GetDefaultValue(component.GetType(), prop.Name);
        if (defaultValue == null)
        {
            return false;
        }

        prop.SetValue(component, defaultValue);
        return true;
    }

    private static object? GetDefaultValue(Type componentType, string propertyName)
    {
        try
        {
            var instance = Activator.CreateInstance(componentType);
            return componentType.GetProperty(propertyName)?.GetValue(instance);
        }
        catch
        {
            return null;
        }
    }

    private static object CreateDefault(Type type)
    {
        if (type == typeof(string))
        {
            return "";
        }

        if (type == typeof(Vector2))
        {
            return Vector2.Zero;
        }

        if (type == typeof(Vector3))
        {
            return Vector3.Zero;
        }

        return type == typeof(Vector4) ? Vector4.Zero : Activator.CreateInstance(type) ?? new object();
    }

    private static string SplitPascalCase(string input)
    {
        return Regex.Replace(input, "(?<!^)([A-Z])", " $1");
    }
}