using Cherris.Components;
using Cherris.Utils;
using CherrisEditor.Undo;
using ImGuiNET;
using System.Numerics;
using System.Reflection;
using System.Text.RegularExpressions;

namespace CherrisEditor.Inspectors;

public sealed class DefaultInspector
{
    private readonly EditorTextureManager _textures;
    private readonly HistoryManager _history;
    private readonly PropertyDrawer _propertyDrawer;

    public DefaultInspector(Editor editor, EditorTextureManager textures, HistoryManager history)
    {
        _textures = textures;
        _history = history;
        _propertyDrawer = new PropertyDrawer(editor, textures, history);
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

        IEnumerable<PropertyInfo> properties = type.GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Where(p => p.CanRead && p.CanWrite && !p.IsDefined(typeof(HideInInspectorAttribute), false));

        foreach (PropertyInfo? prop in properties)
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
        ImGui.Text(FormatPropertyName(prop.Name));

        ImGui.TableSetColumnIndex(1);
        ImGui.PushItemWidth(-1);

        _propertyDrawer.Draw(component, prop);
        ImGui.PopItemWidth();

        ImGui.TableSetColumnIndex(2);
        if (DrawResetButton(component, prop))
        {
            return true;
        }

        return ImGui.IsItemDeactivatedAfterEdit();
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
            object? instance = Activator.CreateInstance(componentType);
            return componentType.GetProperty(propertyName)?.GetValue(instance);
        }
        catch
        {
            return null;
        }
    }

    private static string FormatPropertyName(string input)
    {
        return Regex.Replace(input, "(?<!^)([A-Z])", " $1");
    }
}