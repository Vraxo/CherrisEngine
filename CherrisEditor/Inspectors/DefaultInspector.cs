using Cherris;
using Cherris.Components;
using CherrisEditor.UI;
using CherrisEditor.Undo;
using CherrisEditor.Undo.Commands;
using ImGuiNET;
using System.Numerics;
using System.Reflection;

namespace CherrisEditor.Inspectors;

public class DefaultInspector : IComponentInspector
{
    private readonly EditorTextureManager _textureManager;
    private readonly HistoryManager _history;
    private object? _undoInitialValue = null!;

    public DefaultInspector(EditorTextureManager textureManager, HistoryManager history)
    {
        _textureManager = textureManager;
        _history = history;
    }

    public bool Draw(Component component)
    {
        bool dirty = false;
        Type componentType = component.GetType();

        if (!ImGui.BeginTable(componentType.Name + "Table", 3))
        {
            return false;
        }

        ImGui.TableSetupColumn("Property", ImGuiTableColumnFlags.WidthStretch, 0.475f);
        ImGui.TableSetupColumn("Value", ImGuiTableColumnFlags.WidthStretch, 0.475f);
        ImGui.TableSetupColumn("##Reset", ImGuiTableColumnFlags.WidthStretch, 0.05f);

        var properties = componentType.GetProperties();
        IntPtr resetIcon = _textureManager.GetTexture("Reset");
        float buttonSize = ImGui.GetFrameHeight() - 4;

        foreach (var prop in properties)
        {
            if (!prop.CanRead || !prop.CanWrite || prop.IsDefined(typeof(HideInInspectorAttribute), false))
            {
                continue;
            }

            ImGui.TableNextRow();

            ImGui.TableSetColumnIndex(0);
            ImGui.Text(ImGuiPropertyControls.SplitPascalCase(prop.Name));

            ImGui.TableSetColumnIndex(1);
            ImGui.PushItemWidth(-1.0f);

            object valueBeforeEdit = prop.GetValue(component);
            if (DrawPropertyControl(component, prop, valueBeforeEdit, out bool activated, out bool deactivated))
            {
                dirty = true;
            }

            if (activated)
            {
                _undoInitialValue = valueBeforeEdit;
            }

            if (deactivated && _undoInitialValue != null)
            {
                object valueAfterEdit = prop.GetValue(component);
                if (!_undoInitialValue.Equals(valueAfterEdit))
                {
                    prop.SetValue(component, _undoInitialValue);
                    _history.Execute(new ChangePropertyCommand(component, prop, _undoInitialValue, valueAfterEdit));
                }
                _undoInitialValue = null;
            }

            ImGui.PopItemWidth();

            ImGui.TableSetColumnIndex(2);
            if (ImGui.ImageButton($"Reset##{prop.Name}", resetIcon, new Vector2(buttonSize, buttonSize)))
            {
                ResetToDefault(component, componentType, prop);
                dirty = true;
            }
        }

        ImGui.EndTable();
        return dirty;
    }

    private bool DrawPropertyControl(object instance, PropertyInfo prop, object currentValue, out bool activated, out bool deactivated)
    {
        activated = false;
        deactivated = false;

        if (TryDrawFloat(instance, prop, currentValue, ref activated, ref deactivated))
        {
            return true;
        }

        if (TryDrawInt(instance, prop, currentValue, ref activated, ref deactivated))
        {
            return true;
        }

        if (TryDrawBool(instance, prop, currentValue, ref activated, ref deactivated))
        {
            return true;
        }

        if (TryDrawString(instance, prop, currentValue, ref activated, ref deactivated))
        {
            return true;
        }

        if (TryDrawEnum(instance, prop, currentValue, ref activated, ref deactivated))
        {
            return true;
        }

        if (TryDrawVector2(instance, prop, currentValue, ref activated, ref deactivated))
        {
            return true;
        }

        if (TryDrawVector3(instance, prop, currentValue, ref activated, ref deactivated))
        {
            return true;
        }

        if (TryDrawVector4(instance, prop, currentValue, ref activated, ref deactivated))
        {
            return true;
        }

        ImGui.Text(currentValue?.ToString() ?? "null");
        activated = ImGui.IsItemActivated();
        deactivated = ImGui.IsItemDeactivatedAfterEdit();

        return false;
    }

    private bool TryDrawFloat(object instance, PropertyInfo prop, object currentValue, ref bool activated, ref bool deactivated)
    {
        if (prop.PropertyType != typeof(float))
        {
            return false;
        }

        ImGui.PushID(prop.Name);
        float value = (float)currentValue;

        if (ImGui.IsItemActivated())
        {
            activated = true;
        }

        bool changed = ImGui.DragFloat("##val", ref value, 0.01f);
        if (changed)
        {
            prop.SetValue(instance, value);
        }

        if (ImGui.IsItemDeactivatedAfterEdit())
        {
            deactivated = true;
        }

        ImGui.PopID();
        return changed;
    }

    private bool TryDrawInt(object instance, PropertyInfo prop, object currentValue, ref bool activated, ref bool deactivated)
    {
        if (prop.PropertyType != typeof(int))
        {
            return false;
        }

        ImGui.PushID(prop.Name);
        int value = (int)currentValue;

        if (ImGui.IsItemActivated())
        {
            activated = true;
        }

        bool changed = ImGui.DragInt("##val", ref value);
        if (changed)
        {
            prop.SetValue(instance, value);
        }

        if (ImGui.IsItemDeactivatedAfterEdit())
        {
            deactivated = true;
        }

        ImGui.PopID();
        return changed;
    }

    private bool TryDrawBool(object instance, PropertyInfo prop, object currentValue, ref bool activated, ref bool deactivated)
    {
        if (prop.PropertyType != typeof(bool))
        {
            return false;
        }

        ImGui.PushID(prop.Name);
        bool value = (bool)currentValue;

        if (ImGui.IsItemActivated())
        {
            activated = true;
        }

        bool changed = ImGui.Checkbox("##val", ref value);
        if (changed)
        {
            prop.SetValue(instance, value);
        }

        if (ImGui.IsItemDeactivatedAfterEdit())
        {
            deactivated = true;
        }

        ImGui.PopID();
        return changed;
    }

    private bool TryDrawString(object instance, PropertyInfo prop, object currentValue, ref bool activated, ref bool deactivated)
    {
        if (prop.PropertyType != typeof(string))
        {
            return false;
        }

        ImGui.PushID(prop.Name);
        string value = (string)currentValue ?? "";

        if (ImGui.IsItemActivated())
        {
            activated = true;
        }

        bool changed = ImGui.InputText("##val", ref value, 256);
        if (changed)
        {
            prop.SetValue(instance, value);
        }

        if (ImGui.IsItemDeactivatedAfterEdit())
        {
            deactivated = true;
        }

        ImGui.PopID();
        return changed;
    }

    private bool TryDrawEnum(object instance, PropertyInfo prop, object currentValue, ref bool activated, ref bool deactivated)
    {
        if (!prop.PropertyType.IsEnum)
        {
            return false;
        }

        ImGui.PushID(prop.Name);
        var names = Enum.GetNames(prop.PropertyType);
        int currentIndex = Array.IndexOf(names, currentValue.ToString());

        if (ImGui.IsItemActivated())
        {
            activated = true;
        }

        bool changed = ImGui.Combo("##val", ref currentIndex, names, names.Length);
        if (changed)
        {
            prop.SetValue(instance, Enum.Parse(prop.PropertyType, names[currentIndex]));
        }

        if (ImGui.IsItemDeactivatedAfterEdit())
        {
            deactivated = true;
        }

        ImGui.PopID();
        return changed;
    }

    private bool TryDrawVector2(object instance, PropertyInfo prop, object currentValue, ref bool activated, ref bool deactivated)
    {
        if (prop.PropertyType != typeof(Vector2))
        {
            return false;
        }

        var value = (Vector2)currentValue;
        bool changed = ImGuiPropertyControls.DrawVector2Control("##val", ref value, out bool subActivated, out bool subDeactivated);
        if (changed)
        {
            prop.SetValue(instance, value);
        }

        activated = subActivated;
        deactivated = subDeactivated;
        return changed;
    }

    private bool TryDrawVector3(object instance, PropertyInfo prop, object currentValue, ref bool activated, ref bool deactivated)
    {
        if (prop.PropertyType != typeof(Vector3))
        {
            return false;
        }

        var value = (Vector3)currentValue;
        bool isColor = prop.Name.Contains("Color", StringComparison.OrdinalIgnoreCase);

        bool changed = isColor
            ? ImGuiPropertyControls.DrawColor3Control("##val", ref value, out bool subActivated, out bool subDeactivated)
            : ImGuiPropertyControls.DrawVector3Control("##val", ref value, out subActivated, out subDeactivated);

        if (changed)
        {
            prop.SetValue(instance, value);
        }

        activated = subActivated;
        deactivated = subDeactivated;
        return changed;
    }

    private bool TryDrawVector4(object instance, PropertyInfo prop, object currentValue, ref bool activated, ref bool deactivated)
    {
        if (prop.PropertyType != typeof(Vector4))
        {
            return false;
        }

        var value = (Vector4)currentValue;
        bool isColor = prop.Name.Contains("Color", StringComparison.OrdinalIgnoreCase);

        bool changed;
        if (isColor)
        {
            changed = ImGuiPropertyControls.DrawColor4Control("##val", ref value, out activated, out deactivated);
        }
        else
        {
            ImGui.PushID(prop.Name);
            if (ImGui.IsItemActivated())
            {
                activated = true;
            }

            changed = ImGui.DragFloat4("##val", ref value, 0.1f);
            if (changed)
            {
                prop.SetValue(instance, value);
            }

            if (ImGui.IsItemDeactivatedAfterEdit())
            {
                deactivated = true;
            }

            ImGui.PopID();
        }

        return changed;
    }

    private void ResetToDefault(object component, Type componentType, PropertyInfo prop)
    {
        var defaultValue = GetDefaultValue(componentType, prop.Name);
        if (defaultValue == null)
        {
            return;
        }

        var currentValue = prop.GetValue(component);
        if (currentValue?.Equals(defaultValue) ?? false)
        {
            return;
        }

        prop.SetValue(component, defaultValue);
        _history.Execute(new ChangePropertyCommand(component, prop, currentValue, defaultValue));
    }

    private static object? GetDefaultValue(Type componentType, string propertyName)
    {
        if (!_defaultComponentCache.TryGetValue(componentType, out object defaultInstance))
        {
            try
            {
                if (componentType.GetConstructor(Type.EmptyTypes) is not null)
                {
                    defaultInstance = Activator.CreateInstance(componentType);
                    _defaultComponentCache[componentType] = defaultInstance;
                }
            }
            catch { return null!; }
        }

        return defaultInstance is not null ? (componentType.GetProperty(propertyName)?.GetValue(defaultInstance)) : null;
    }

    private static readonly Dictionary<Type, object> _defaultComponentCache = [];
}