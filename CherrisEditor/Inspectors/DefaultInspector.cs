using Cherris;
using Cherris.Components;
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

        bool changed = _propertyDrawer.Draw(component, prop, out _, out _);
        ImGui.PopItemWidth();

        ImGui.TableSetColumnIndex(2);
        if (DrawResetButton(component, prop))
        {
            changed = true;
        }

        return changed;
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

    private static string SplitPascalCase(string input)
    {
        return Regex.Replace(input, "(?<!^)([A-Z])", " $1");
    }
}