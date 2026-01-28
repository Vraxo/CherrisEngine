using Cherris;
using Cherris.Components;
using CherrisEditor.Undo;
using CherrisEditor.Undo.Commands;
using ImGuiNET;
using System.Numerics;
using System.Reflection;
using System.Text.RegularExpressions;

namespace CherrisEditor.Inspectors;

public class DefaultInspector : IComponentInspector
{
    private static readonly Dictionary<Type, object> _defaultComponentCache = [];
    private readonly EditorTextureManager _textureManager;
    private readonly HistoryManager _history;
    private object? _undoInitialValue;

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

        var properties = componentType.GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Where(p => p.CanRead && p.CanWrite && p.IsDefined(typeof(HideInInspectorAttribute)) is false);

        IntPtr resetIcon = _textureManager.GetTexture("Reset");
        float buttonSize = ImGui.GetFrameHeight() - 4;

        foreach (var prop in properties)
        {
            ImGui.TableNextRow();
            _ = ImGui.TableSetColumnIndex(0);
            ImGui.Text(SplitPascalCase(prop.Name));

            _ = ImGui.TableSetColumnIndex(1);
            ImGui.PushItemWidth(-1.0f);

            object valueBeforeEdit = prop.GetValue(component);
            if (DrawPropertyControl(component, prop, out bool activated, out bool deactivated))
            {
                dirty = true;
            }

            if (activated)
            {
                _undoInitialValue = valueBeforeEdit;
            }

            if (deactivated)
            {
                object valueAfterEdit = prop.GetValue(component);
                if (_undoInitialValue is not null && !_undoInitialValue.Equals(valueAfterEdit))
                {
                    prop.SetValue(component, _undoInitialValue);
                    _history.Execute(new ChangePropertyCommand(component, prop, _undoInitialValue, valueAfterEdit));
                }
                _undoInitialValue = null;
            }

            ImGui.PopItemWidth();

            _ = ImGui.TableSetColumnIndex(2);
            if (ImGui.ImageButton($"Reset##{prop.Name}", resetIcon, new Vector2(buttonSize, buttonSize)))
            {
                object defaultValue = GetDefaultValue(componentType, prop.Name);
                if (defaultValue is not null)
                {
                    prop.SetValue(component, defaultValue);
                    dirty = true;
                }
            }
        }

        ImGui.EndTable();
        return dirty;
    }

    private bool DrawPropertyControl(object instance, PropertyInfo prop, out bool activated, out bool deactivated)
    {
        object currentValue = prop.GetValue(instance);
        bool valueChanged = false;
        activated = false;
        deactivated = false;

        ImGui.PushID(prop.Name);
        Type type = prop.PropertyType;

        if (type == typeof(float))
        {
            valueChanged = DrawFloat(prop, currentValue, out activated, out deactivated);
        }
        else if (type == typeof(int))
        {
            valueChanged = DrawInt(prop, currentValue, out activated, out deactivated);
        }
        else if (type == typeof(bool))
        {
            valueChanged = DrawBool(prop, currentValue, out activated, out deactivated);
        }
        else if (type == typeof(string))
        {
            valueChanged = DrawString(prop, currentValue, out activated, out deactivated);
        }
        else if (type.IsEnum)
        {
            valueChanged = DrawEnum(prop, type, currentValue, out activated, out deactivated);
        }
        else if (type == typeof(Vector2))
        {
            valueChanged = DrawVector2(prop, currentValue, out activated, out deactivated);
        }
        else if (type == typeof(Vector3))
        {
            valueChanged = DrawVector3(prop, currentValue, out activated, out deactivated);
        }
        else if (type == typeof(Vector4))
        {
            valueChanged = DrawVector4(prop, currentValue, out activated, out deactivated);
        }
        else
        {
            ImGui.Text(currentValue?.ToString() ?? "null");
        }

        if (!activated && !deactivated)
        {
            activated = ImGui.IsItemActivated();
            deactivated = ImGui.IsItemDeactivatedAfterEdit();
        }

        if (valueChanged)
        {
            instance.GetType().GetProperty(prop.Name)?.SetValue(instance, currentValue);
        }

        ImGui.PopID();
        return valueChanged;
    }

    private bool DrawFloat(PropertyInfo prop, object currentValue, out bool activated, out bool deactivated)
    {
        float val = (float)currentValue;
        bool changed = ImGui.DragFloat($"##{prop.Name}", ref val, 0.01f);
        activated = ImGui.IsItemActivated();
        deactivated = ImGui.IsItemDeactivatedAfterEdit();
        if (changed)
        {
            prop.SetValue(prop.GetType(), val);
        }

        return changed;
    }

    private bool DrawInt(PropertyInfo prop, object currentValue, out bool activated, out bool deactivated)
    {
        int val = (int)currentValue;
        bool changed = ImGui.DragInt($"##{prop.Name}", ref val);
        activated = ImGui.IsItemActivated();
        deactivated = ImGui.IsItemDeactivatedAfterEdit();
        if (changed)
        {
            prop.SetValue(prop.GetType(), val);
        }

        return changed;
    }

    private bool DrawBool(PropertyInfo prop, object currentValue, out bool activated, out bool deactivated)
    {
        bool val = (bool)currentValue;
        bool changed = ImGui.Checkbox($"##{prop.Name}", ref val);
        activated = ImGui.IsItemActivated();
        deactivated = ImGui.IsItemDeactivatedAfterEdit();
        if (changed)
        {
            prop.SetValue(prop.GetType(), val);
        }

        return changed;
    }

    private bool DrawString(PropertyInfo prop, object currentValue, out bool activated, out bool deactivated)
    {
        string val = (string)currentValue ?? "";
        bool changed = ImGui.InputText($"##{prop.Name}", ref val, 256);
        activated = ImGui.IsItemActivated();
        deactivated = ImGui.IsItemDeactivatedAfterEdit();
        if (changed)
        {
            prop.SetValue(prop.GetType(), val);
        }

        return changed;
    }

    private bool DrawEnum(PropertyInfo prop, Type enumType, object currentValue, out bool activated, out bool deactivated)
    {
        var enumValues = Enum.GetNames(enumType);
        string currentEnumValue = currentValue.ToString();
        int currentIndex = Array.IndexOf(enumValues, currentEnumValue);
        bool changed = ImGui.Combo($"##{prop.Name}", ref currentIndex, enumValues, enumValues.Length);
        activated = ImGui.IsItemActivated();
        deactivated = ImGui.IsItemDeactivatedAfterEdit();
        if (changed)
        {
            prop.SetValue(prop.GetType(), Enum.Parse(enumType, enumValues[currentIndex]));
        }

        return changed;
    }

    private bool DrawVector2(PropertyInfo prop, object currentValue, out bool activated, out bool deactivated)
    {
        var val = (Vector2)currentValue;
        ImGui.PopItemWidth();
        bool changed = DefaultInspector.DrawVector2Control($"##{prop.Name}", ref val, out activated, out deactivated);
        ImGui.PushItemWidth(-1.0f);
        if (changed)
        {
            prop.SetValue(prop.GetType(), val);
        }

        return changed;
    }

    private bool DrawVector3(PropertyInfo prop, object currentValue, out bool activated, out bool deactivated)
    {
        var val = (Vector3)currentValue;
        ImGui.PopItemWidth();
        bool changed = prop.Name.Contains("Color", StringComparison.OrdinalIgnoreCase)
            ? DefaultInspector.DrawColor3Control($"##{prop.Name}", ref val, out activated, out deactivated)
            : DefaultInspector.DrawVector3Control($"##{prop.Name}", ref val, out activated, out deactivated);
        ImGui.PushItemWidth(-1.0f);
        if (changed)
        {
            prop.SetValue(prop.GetType(), val);
        }

        return changed;
    }

    private bool DrawVector4(PropertyInfo prop, object currentValue, out bool activated, out bool deactivated)
    {
        var val = (Vector4)currentValue;
        ImGui.PopItemWidth();
        bool changed;
        if (prop.Name.Contains("Color", StringComparison.OrdinalIgnoreCase))
        {
            changed = DefaultInspector.DrawColor4Control($"##{prop.Name}", ref val, out activated, out deactivated);
        }
        else
        {
            changed = ImGui.DragFloat4($"##{prop.Name}", ref val, 0.1f);
            activated = ImGui.IsItemActivated();
            deactivated = ImGui.IsItemDeactivatedAfterEdit();
        }
        ImGui.PushItemWidth(-1.0f);
        if (changed)
        {
            prop.SetValue(prop.GetType(), val);
        }

        return changed;
    }

    public static bool DrawVector2Control(string label, ref Vector2 values, out bool activated, out bool deactivated)
    {
        bool valueChanged = false;
        activated = false;
        deactivated = false;

        ImGui.PushID(label);
        var style = ImGui.GetStyle();
        float itemWidth = (ImGui.GetContentRegionAvail().X - (style.ItemSpacing.X * 3) - (ImGui.CalcTextSize("X").X + ImGui.CalcTextSize("Y").X)) / 2.0f;

        ImGui.AlignTextToFramePadding();
        ImGui.PushStyleColor(ImGuiCol.Text, new Vector4(0.8f, 0.2f, 0.2f, 1.0f)); ImGui.Text("X"); ImGui.PopStyleColor(); ImGui.SameLine();
        ImGui.PushItemWidth(itemWidth);
        if (ImGui.DragFloat($"##{label}X", ref values.X, 0.1f))
        {
            valueChanged = true;
        }

        if (ImGui.IsItemActivated())
        {
            activated = true;
        }

        if (ImGui.IsItemDeactivatedAfterEdit())
        {
            deactivated = true;
        }

        ImGui.PopItemWidth(); ImGui.SameLine();

        ImGui.AlignTextToFramePadding();
        ImGui.PushStyleColor(ImGuiCol.Text, new Vector4(0.2f, 0.8f, 0.2f, 1.0f)); ImGui.Text("Y"); ImGui.PopStyleColor(); ImGui.SameLine();
        ImGui.PushItemWidth(itemWidth);
        if (ImGui.DragFloat($"##{label}Y", ref values.Y, 0.1f))
        {
            valueChanged = true;
        }

        if (ImGui.IsItemActivated())
        {
            activated = true;
        }

        if (ImGui.IsItemDeactivatedAfterEdit())
        {
            deactivated = true;
        }

        ImGui.PopItemWidth();

        ImGui.PopID();

        return valueChanged;
    }

    public static bool DrawVector3Control(string label, ref Vector3 values, out bool activated, out bool deactivated)
    {
        bool valueChanged = false;
        activated = false;
        deactivated = false;

        ImGui.PushID(label);
        var style = ImGui.GetStyle();
        float itemWidth = (ImGui.GetContentRegionAvail().X - (style.ItemSpacing.X * 5) - (ImGui.CalcTextSize("X").X * 3)) / 3.0f;

        ImGui.AlignTextToFramePadding();
        ImGui.PushStyleColor(ImGuiCol.Text, new Vector4(0.8f, 0.2f, 0.2f, 1.0f)); ImGui.Text("X"); ImGui.PopStyleColor(); ImGui.SameLine();
        ImGui.PushItemWidth(itemWidth);
        if (ImGui.DragFloat($"##{label}X", ref values.X, 0.1f))
        {
            valueChanged = true;
        }

        if (ImGui.IsItemActivated())
        {
            activated = true;
        }

        if (ImGui.IsItemDeactivatedAfterEdit())
        {
            deactivated = true;
        }

        ImGui.PopItemWidth(); ImGui.SameLine();

        ImGui.AlignTextToFramePadding();
        ImGui.PushStyleColor(ImGuiCol.Text, new Vector4(0.2f, 0.8f, 0.2f, 1.0f)); ImGui.Text("Y"); ImGui.PopStyleColor(); ImGui.SameLine();
        ImGui.PushItemWidth(itemWidth);
        if (ImGui.DragFloat($"##{label}Y", ref values.Y, 0.1f))
        {
            valueChanged = true;
        }

        if (ImGui.IsItemActivated())
        {
            activated = true;
        }

        if (ImGui.IsItemDeactivatedAfterEdit())
        {
            deactivated = true;
        }

        ImGui.PopItemWidth(); ImGui.SameLine();

        ImGui.AlignTextToFramePadding();
        ImGui.PushStyleColor(ImGuiCol.Text, new Vector4(0.2f, 0.3f, 0.8f, 1.0f)); ImGui.Text("Z"); ImGui.PopStyleColor(); ImGui.SameLine();
        ImGui.PushItemWidth(itemWidth);
        if (ImGui.DragFloat($"##{label}Z", ref values.Z, 0.1f))
        {
            valueChanged = true;
        }

        if (ImGui.IsItemActivated())
        {
            activated = true;
        }

        if (ImGui.IsItemDeactivatedAfterEdit())
        {
            deactivated = true;
        }

        ImGui.PopItemWidth();
        ImGui.PopID();

        return valueChanged;
    }

    public static bool DrawColor3Control(string label, ref Vector3 color, out bool activated, out bool deactivated)
    {
        bool valueChanged = false;
        activated = false;
        deactivated = false;

        ImGui.PushID(label);

        if (ImGui.ColorEdit3(label, ref color, ImGuiColorEditFlags.Float | ImGuiColorEditFlags.HDR))
        {
            valueChanged = true;
        }

        if (ImGui.IsItemActivated())
        {
            activated = true;
        }

        if (ImGui.IsItemDeactivatedAfterEdit())
        {
            deactivated = true;
        }

        ImGui.PopID();

        return valueChanged;
    }

    public static bool DrawColor4Control(string label, ref Vector4 color, out bool activated, out bool deactivated)
    {
        bool valueChanged = false;
        activated = false;
        deactivated = false;

        ImGui.PushID(label);

        if (ImGui.ColorEdit4(label, ref color, ImGuiColorEditFlags.Float | ImGuiColorEditFlags.HDR))
        {
            valueChanged = true;
        }

        if (ImGui.IsItemActivated())
        {
            activated = true;
        }

        if (ImGui.IsItemDeactivatedAfterEdit())
        {
            deactivated = true;
        }

        ImGui.PopID();

        return valueChanged;
    }

    private object? GetDefaultValue(Type componentType, string propertyName)
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
            catch { return null; }
        }

        return defaultInstance is not null ? (componentType.GetProperty(propertyName)?.GetValue(defaultInstance)) : null;
    }

    private static string SplitPascalCase(string input)
    {
        return Regex.Replace(input, "(?<!^)([A-Z])", " $1");
    }
}