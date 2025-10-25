using Cherris;
using ImGuiNET;
using System;
using System.Collections.Generic;
using System.Numerics;
using System.Reflection;
using System.Text.RegularExpressions;
using CherrisEditor.Undo;
using CherrisEditor.Undo.Commands;

namespace CherrisEditor.Inspectors;

public class DefaultInspector : IComponentInspector
{
    private static readonly Dictionary<Type, object> _defaultComponentCache = new();
    private readonly EditorTextureManager _textureManager;
    private readonly HistoryManager _history;
    private object _undoInitialValue;

    public DefaultInspector(EditorTextureManager textureManager, HistoryManager history)
    {
        _textureManager = textureManager;
        _history = history;
    }

    public bool Draw(Component component)
    {
        bool dirty = false;
        Type componentType = component.GetType();
        if (!ImGui.BeginTable(componentType.Name + "Table", 3)) return false;

        ImGui.TableSetupColumn("Property", ImGuiTableColumnFlags.WidthStretch, 0.475f);
        ImGui.TableSetupColumn("Value", ImGuiTableColumnFlags.WidthStretch, 0.475f);
        ImGui.TableSetupColumn("##Reset", ImGuiTableColumnFlags.WidthStretch, 0.05f);

        var properties = componentType.GetProperties(BindingFlags.Public | BindingFlags.Instance);
        IntPtr resetIcon = _textureManager.GetTexture("Reset");
        float buttonSize = ImGui.GetFrameHeight() - 4; // A bit of padding

        foreach (var prop in properties)
        {
            if (!prop.CanRead || !prop.CanWrite || prop.IsDefined(typeof(HideInInspectorAttribute), false))
                continue;

            ImGui.TableNextRow();
            ImGui.TableSetColumnIndex(0);
            ImGui.Text(SplitPascalCase(prop.Name));

            ImGui.TableSetColumnIndex(1);
            ImGui.PushItemWidth(-1.0f);
            if (DrawPropertyControl(component, prop))
            {
                dirty = true;
            }
            ImGui.PopItemWidth();

            ImGui.TableSetColumnIndex(2);
            if (ImGui.ImageButton($"Reset##{prop.Name}", resetIcon, new Vector2(buttonSize, buttonSize)))
            {
                object defaultValue = GetDefaultValue(componentType, prop.Name);
                if (defaultValue != null)
                {
                    prop.SetValue(component, defaultValue);
                    dirty = true;
                }
            }
        }

        ImGui.EndTable();
        return dirty;
    }

    private bool DrawPropertyControl(object instance, PropertyInfo prop)
    {
        object currentValue = prop.GetValue(instance);
        bool valueChanged = false;

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
        else if (prop.PropertyType == typeof(Vector2))
        {
            var val = (Vector2)currentValue;
            ImGui.PopItemWidth(); // Pop the -1 width since the control manages its own
            if (DrawVector2Control($"##{prop.Name}", ref val))
            {
                prop.SetValue(instance, val);
                valueChanged = true;
            }
            ImGui.PushItemWidth(-1.0f); // Push it back
        }
        else if (prop.PropertyType == typeof(Vector3))
        {
            var val = (Vector3)currentValue;
            if (prop.Name.Contains("Color", StringComparison.OrdinalIgnoreCase))
            {
                ImGui.PopItemWidth();
                if (DrawColor3Control($"##{prop.Name}", ref val))
                {
                    prop.SetValue(instance, val);
                    valueChanged = true;
                }
                ImGui.PushItemWidth(-1.0f);
            }
            else if (ImGui.DragFloat3($"##{prop.Name}", ref val, 0.1f))
            {
                prop.SetValue(instance, val);
                valueChanged = true;
            }
        }
        else if (prop.PropertyType == typeof(Vector4))
        {
            var val = (Vector4)currentValue;
            if (prop.Name.Contains("Color", StringComparison.OrdinalIgnoreCase))
            {
                ImGui.PopItemWidth();
                if (DrawColor4Control($"##{prop.Name}", ref val))
                {
                    prop.SetValue(instance, val);
                    valueChanged = true;
                }
                ImGui.PushItemWidth(-1.0f);
            }
            else if (ImGui.DragFloat4($"##{prop.Name}", ref val, 0.1f))
            {
                prop.SetValue(instance, val);
                valueChanged = true;
            }
        }
        else
        {
            ImGui.Text(currentValue?.ToString() ?? "null");
        }

        HandleUndo(instance, prop, currentValue);

        ImGui.PopID();

        return valueChanged;
    }

    private void HandleUndo(object target, PropertyInfo property, object oldValue)
    {
        if (ImGui.IsItemActivated())
        {
            _undoInitialValue = oldValue;
        }

        if (ImGui.IsItemDeactivatedAfterEdit())
        {
            object newValue = property.GetValue(target);
            if (_undoInitialValue != null && !_undoInitialValue.Equals(newValue))
            {
                property.SetValue(target, _undoInitialValue);
                _history.Execute(new ChangePropertyCommand(target, property, _undoInitialValue, newValue));
            }
            _undoInitialValue = null;
        }
    }

    public static bool DrawVector2Control(string label, ref Vector2 values)
    {
        bool valueChanged = false;
        ImGui.PushID(label);
        var style = ImGui.GetStyle();
        float itemWidth = (ImGui.GetContentRegionAvail().X - (style.ItemSpacing.X * 3) - (ImGui.CalcTextSize("X").X + ImGui.CalcTextSize("Y").X)) / 2.0f;

        // X
        ImGui.AlignTextToFramePadding();
        ImGui.PushStyleColor(ImGuiCol.Text, new Vector4(0.8f, 0.2f, 0.2f, 1.0f)); ImGui.Text("X"); ImGui.PopStyleColor(); ImGui.SameLine();
        ImGui.PushItemWidth(itemWidth); if (ImGui.DragFloat($"##{label}X", ref values.X, 0.1f)) valueChanged = true; ImGui.PopItemWidth(); ImGui.SameLine();

        // Y
        ImGui.AlignTextToFramePadding();
        ImGui.PushStyleColor(ImGuiCol.Text, new Vector4(0.2f, 0.8f, 0.2f, 1.0f)); ImGui.Text("Y"); ImGui.PopStyleColor(); ImGui.SameLine();
        ImGui.PushItemWidth(itemWidth); if (ImGui.DragFloat($"##{label}Y", ref values.Y, 0.1f)) valueChanged = true; ImGui.PopItemWidth();

        ImGui.PopID();

        return valueChanged;
    }

    public static bool DrawColor3Control(string label, ref Vector3 color)
    {
        bool valueChanged = false;
        ImGui.PushID(label);
        var style = ImGui.GetStyle();
        float itemWidth = (ImGui.GetContentRegionAvail().X - (style.ItemSpacing.X * 5) - (ImGui.CalcTextSize("R").X + ImGui.CalcTextSize("G").X + ImGui.CalcTextSize("B").X)) / 3.0f;

        // R
        ImGui.AlignTextToFramePadding();
        ImGui.PushStyleColor(ImGuiCol.Text, new Vector4(0.8f, 0.2f, 0.2f, 1.0f)); ImGui.Text("R"); ImGui.PopStyleColor(); ImGui.SameLine();
        ImGui.PushItemWidth(itemWidth); if (ImGui.DragFloat($"##{label}R", ref color.X, 0.1f)) valueChanged = true; ImGui.PopItemWidth(); ImGui.SameLine();

        // G
        ImGui.AlignTextToFramePadding();
        ImGui.PushStyleColor(ImGuiCol.Text, new Vector4(0.2f, 0.8f, 0.2f, 1.0f)); ImGui.Text("G"); ImGui.PopStyleColor(); ImGui.SameLine();
        ImGui.PushItemWidth(itemWidth); if (ImGui.DragFloat($"##{label}G", ref color.Y, 0.1f)) valueChanged = true; ImGui.PopItemWidth(); ImGui.SameLine();

        // B
        ImGui.AlignTextToFramePadding();
        ImGui.PushStyleColor(ImGuiCol.Text, new Vector4(0.2f, 0.3f, 0.8f, 1.0f)); ImGui.Text("B"); ImGui.PopStyleColor(); ImGui.SameLine();
        ImGui.PushItemWidth(itemWidth); if (ImGui.DragFloat($"##{label}B", ref color.Z, 0.1f)) valueChanged = true; ImGui.PopItemWidth();

        ImGui.PopID();

        return valueChanged;
    }

    public static bool DrawColor4Control(string label, ref Vector4 color)
    {
        bool valueChanged = false;
        ImGui.PushID(label);
        var style = ImGui.GetStyle();
        float itemWidth = (ImGui.GetContentRegionAvail().X - (style.ItemSpacing.X * 7) - (ImGui.CalcTextSize("R").X + ImGui.CalcTextSize("G").X + ImGui.CalcTextSize("B").X + ImGui.CalcTextSize("A").X)) / 4.0f;

        // R
        ImGui.AlignTextToFramePadding();
        ImGui.PushStyleColor(ImGuiCol.Text, new Vector4(0.8f, 0.2f, 0.2f, 1.0f)); ImGui.Text("R"); ImGui.PopStyleColor(); ImGui.SameLine();
        ImGui.PushItemWidth(itemWidth); if (ImGui.DragFloat($"##{label}R", ref color.X, 0.01f)) valueChanged = true; ImGui.PopItemWidth(); ImGui.SameLine();

        // G
        ImGui.AlignTextToFramePadding();
        ImGui.PushStyleColor(ImGuiCol.Text, new Vector4(0.2f, 0.8f, 0.2f, 1.0f)); ImGui.Text("G"); ImGui.PopStyleColor(); ImGui.SameLine();
        ImGui.PushItemWidth(itemWidth); if (ImGui.DragFloat($"##{label}G", ref color.Y, 0.01f)) valueChanged = true; ImGui.PopItemWidth(); ImGui.SameLine();

        // B
        ImGui.AlignTextToFramePadding();
        ImGui.PushStyleColor(ImGuiCol.Text, new Vector4(0.2f, 0.3f, 0.8f, 1.0f)); ImGui.Text("B"); ImGui.PopStyleColor(); ImGui.SameLine();
        ImGui.PushItemWidth(itemWidth); if (ImGui.DragFloat($"##{label}B", ref color.Z, 0.01f)) valueChanged = true; ImGui.PopItemWidth(); ImGui.SameLine();

        // A
        ImGui.AlignTextToFramePadding();
        ImGui.PushStyleColor(ImGuiCol.Text, new Vector4(0.8f, 0.8f, 0.8f, 1.0f)); ImGui.Text("A"); ImGui.PopStyleColor(); ImGui.SameLine();
        ImGui.PushItemWidth(itemWidth); if (ImGui.DragFloat($"##{label}A", ref color.W, 0.01f)) valueChanged = true; ImGui.PopItemWidth();

        ImGui.PopID();

        return valueChanged;
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