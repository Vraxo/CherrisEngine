using Cherris;
using ImGuiNET;
using System;
using System.Collections.Generic;
using System.Numerics;
using System.Reflection;
using System.Text.RegularExpressions;

namespace CherrisEditor.Inspectors;

public class DefaultInspector : IComponentInspector
{
    private static readonly Dictionary<Type, object> _defaultComponentCache = new();

    public void Draw(Component component)
    {
        Type componentType = component.GetType();
        if (!ImGui.BeginTable(componentType.Name + "Table", 3, ImGuiTableFlags.Resizable)) return;

        ImGui.TableSetupColumn("Property", ImGuiTableColumnFlags.WidthFixed, 120.0f);
        ImGui.TableSetupColumn("Value", ImGuiTableColumnFlags.WidthStretch);
        ImGui.TableSetupColumn("##Reset", ImGuiTableColumnFlags.WidthFixed, 25.0f);

        var properties = componentType.GetProperties(BindingFlags.Public | BindingFlags.Instance);

        foreach (var prop in properties)
        {
            if (!prop.CanRead || !prop.CanWrite || prop.IsDefined(typeof(HideInInspectorAttribute), false))
                continue;

            ImGui.TableNextRow();
            ImGui.TableSetColumnIndex(0);
            ImGui.Text(SplitPascalCase(prop.Name));

            ImGui.TableSetColumnIndex(1);
            ImGui.PushItemWidth(-1.0f);
            DrawPropertyControl(component, prop);
            ImGui.PopItemWidth();

            ImGui.TableSetColumnIndex(2);
            if (ImGui.Button($"R##{prop.Name}"))
            {
                object defaultValue = GetDefaultValue(componentType, prop.Name);
                if (defaultValue != null)
                {
                    prop.SetValue(component, defaultValue);
                }
            }
        }

        ImGui.EndTable();
    }

    private void DrawPropertyControl(object instance, PropertyInfo prop)
    {
        object currentValue = prop.GetValue(instance);

        if (prop.PropertyType == typeof(float))
        {
            float val = (float)currentValue;
            if (ImGui.DragFloat($"##{prop.Name}", ref val, 0.01f))
            {
                prop.SetValue(instance, val);
            }
        }
        else if (prop.PropertyType == typeof(int))
        {
            int val = (int)currentValue;
            if (ImGui.DragInt($"##{prop.Name}", ref val))
            {
                prop.SetValue(instance, val);
            }
        }
        else if (prop.PropertyType == typeof(bool))
        {
            bool val = (bool)currentValue;
            if (ImGui.Checkbox($"##{prop.Name}", ref val))
            {
                prop.SetValue(instance, val);
            }
        }
        else if (prop.PropertyType == typeof(string))
        {
            string val = (string)currentValue ?? "";
            if (ImGui.InputText($"##{prop.Name}", ref val, 256))
            {
                prop.SetValue(instance, val);
            }
        }
        else if (prop.PropertyType == typeof(Vector2))
        {
            var val = (Vector2)currentValue;
            if (ImGui.DragFloat2($"##{prop.Name}", ref val, 0.1f))
            {
                prop.SetValue(instance, val);
            }
        }
        else if (prop.PropertyType == typeof(Vector3))
        {
            var val = (Vector3)currentValue;
            if (ImGui.DragFloat3($"##{prop.Name}", ref val, 0.1f))
            {
                prop.SetValue(instance, val);
            }
        }
        else
        {
            ImGui.Text(currentValue?.ToString() ?? "null");
        }
    }

    private object GetDefaultValue(Type componentType, string propertyName)
    {
        if (!_defaultComponentCache.TryGetValue(componentType, out object defaultInstance))
        {
            try
            {
                defaultInstance = Activator.CreateInstance(componentType);
                _defaultComponentCache[componentType] = defaultInstance;
            }
            catch { return null; }
        }
        return componentType.GetProperty(propertyName)?.GetValue(defaultInstance);
    }

    private static string SplitPascalCase(string input)
    {
        return Regex.Replace(input, "(?<!^)([A-Z])", " $1");
    }
}