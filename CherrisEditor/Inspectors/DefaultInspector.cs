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

        var properties = componentType.GetProperties(BindingFlags.Public | BindingFlags.Instance);
        IntPtr resetIcon = _textureManager.GetTexture("Reset");
        float buttonSize = ImGui.GetFrameHeight() - 4; // A bit of padding

        foreach (var prop in properties)
        {
            if (!prop.CanRead || !prop.CanWrite || prop.IsDefined(typeof(HideInInspectorAttribute), false))
            {
                continue;
            }

            ImGui.TableNextRow();
            ImGui.TableSetColumnIndex(0);
            ImGui.Text(SplitPascalCase(prop.Name));

            ImGui.TableSetColumnIndex(1);
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

            ImGui.TableSetColumnIndex(2);
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

        if (prop.PropertyType == typeof(float))
        {
            float val = (float)currentValue;
            if (ImGui.DragFloat($"##{prop.Name}", ref val, 0.01f))
            {
                prop.SetValue(instance, val);
                valueChanged = true;
            }
        }
        else if (prop.PropertyType == typeof(int))
        {
            int val = (int)currentValue;
            if (ImGui.DragInt($"##{prop.Name}", ref val))
            {
                prop.SetValue(instance, val);
                valueChanged = true;
            }
        }
        else if (prop.PropertyType == typeof(bool))
        {
            bool val = (bool)currentValue;
            if (ImGui.Checkbox($"##{prop.Name}", ref val))
            {
                prop.SetValue(instance, val);
                valueChanged = true;
            }
        }
        else if (prop.PropertyType == typeof(string))
        {
            string val = (string)currentValue ?? "";
            if (ImGui.InputText($"##{prop.Name}", ref val, 256))
            {
                prop.SetValue(instance, val);
                valueChanged = true;
            }
        }
        else if (prop.PropertyType.IsEnum)
        {
            var enumValues = Enum.GetNames(prop.PropertyType);
            string currentEnumValue = currentValue.ToString();
            int currentIndex = Array.IndexOf(enumValues, currentEnumValue);
            if (ImGui.Combo($"##{prop.Name}", ref currentIndex, enumValues, enumValues.Length))
            {
                prop.SetValue(instance, Enum.Parse(prop.PropertyType, enumValues[currentIndex]));
                valueChanged = true;
            }
        }
        else if (prop.PropertyType == typeof(Vector2))
        {
            var val = (Vector2)currentValue;
            ImGui.PopItemWidth();
            if (DrawVector2Control($"##{prop.Name}", ref val, out activated, out deactivated))
            {
                prop.SetValue(instance, val);
                valueChanged = true;
            }
            ImGui.PushItemWidth(-1.0f);
        }
        else if (prop.PropertyType == typeof(Vector3))
        {
            var val = (Vector3)currentValue;
            ImGui.PopItemWidth();
            if (prop.Name.Contains("Color", StringComparison.OrdinalIgnoreCase))
            {
                if (DrawColor3Control($"##{prop.Name}", ref val, out activated, out deactivated))
                {
                    prop.SetValue(instance, val);
                    valueChanged = true;
                }
            }
            else if (DrawVector3Control($"##{prop.Name}", ref val, out activated, out deactivated))
            {
                prop.SetValue(instance, val);
                valueChanged = true;
            }
            ImGui.PushItemWidth(-1.0f);
        }
        else if (prop.PropertyType == typeof(Vector4))
        {
            var val = (Vector4)currentValue;
            ImGui.PopItemWidth();
            if (prop.Name.Contains("Color", StringComparison.OrdinalIgnoreCase))
            {
                if (DrawColor4Control($"##{prop.Name}", ref val, out activated, out deactivated))
                {
                    prop.SetValue(instance, val);
                    valueChanged = true;
                }
            }
            else if (ImGui.DragFloat4($"##{prop.Name}", ref val, 0.1f))
            {
                prop.SetValue(instance, val);
                valueChanged = true;
            }
            ImGui.PushItemWidth(-1.0f);
        }
        else
        {
            ImGui.Text(currentValue?.ToString() ?? "null");
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

        return valueChanged;
    }

    public static bool DrawVector2Control(string label, ref Vector2 values, out bool activated, out bool deactivated)
    {
        bool valueChanged = false;
        activated = false;
        deactivated = false;

        ImGui.PushID(label);
        var style = ImGui.GetStyle();
        float itemWidth = (ImGui.GetContentRegionAvail().X - (style.ItemSpacing.X * 3) - (ImGui.CalcTextSize("X").X + ImGui.CalcTextSize("Y").X)) / 2.0f;

        // X
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

        // Y
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