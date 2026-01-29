using Cherris.Components;
using CherrisEditor.Undo;
using CherrisEditor.Undo.Commands;
using ImGuiNET;
using System.Numerics;

namespace CherrisEditor.Inspectors;

[CustomInspector(typeof(Light))]
public sealed class LightInspector : IComponentInspector
{
    private readonly EditorTextureManager _textures;
    private readonly HistoryManager _history;
    private readonly UndoTracker _undo;

    public LightInspector(EditorTextureManager textures, HistoryManager history)
    {
        _textures = textures;
        _history = history;
        _undo = new UndoTracker(history);
    }

    public bool Draw(Component component)
    {
        var light = (Light)component;
        bool dirty = false;

        if (!ImGui.BeginTable("LightTable", 3))
        {
            return false;
        }

        ImGui.TableSetupColumn("Property", ImGuiTableColumnFlags.WidthStretch, 0.475f);
        ImGui.TableSetupColumn("Value", ImGuiTableColumnFlags.WidthStretch, 0.475f);
        ImGui.TableSetupColumn("##Reset", ImGuiTableColumnFlags.WidthStretch, 0.05f);

        DrawTypeRow(light, ref dirty);
        DrawColorRow(light, ref dirty);
        DrawFloatRow(light, nameof(Light.Intensity), 0.01f, 0f, float.MaxValue, 1f, ref dirty);

        switch (light.Type)
        {
            case LightType.Directional:
                DrawFloatRow(light, nameof(Light.AmbientStrength), 0.01f, 0f, 1f, 0.3f, ref dirty);
                break;
            case LightType.Point:
                DrawFloatRow(light, nameof(Light.Range), 0.1f, 0f, float.MaxValue, 50f, ref dirty);
                break;
            case LightType.Spot:
                DrawFloatRow(light, nameof(Light.Range), 0.1f, 0f, float.MaxValue, 50f, ref dirty);
                DrawFloatRow(light, nameof(Light.InnerConeAngle), 0.1f, 0f, light.OuterConeAngle, 12.5f, ref dirty);
                DrawFloatRow(light, nameof(Light.OuterConeAngle), 0.1f, light.InnerConeAngle, 89f, 17.5f, ref dirty);
                break;
        }

        ImGui.EndTable();
        return dirty;
    }

    private void DrawTypeRow(Light light, ref bool dirty)
    {
        ImGui.TableNextRow();
        ImGui.TableSetColumnIndex(0);
        ImGui.Text("Type");

        ImGui.TableSetColumnIndex(1);
        ImGui.PushItemWidth(-1);

        LightType typeBefore = light.Type;
        LightType currentType = light.Type;

        if (PropertyDrawer.EnumField("##LightType", ref currentType) && currentType != typeBefore)
        {
            _history.Execute(new ChangePropertyCommand(light, typeof(Light).GetProperty(nameof(Light.Type))!, typeBefore, currentType));
            light.Type = currentType;
            dirty = true;
        }

        ImGui.PopItemWidth();
        ImGui.TableSetColumnIndex(2);
    }

    private static bool DrawEnum<T>(string id, ref T value) where T : struct, Enum
    {
        var names = Enum.GetNames<T>();
        string current = value.ToString();
        int index = Array.IndexOf(names, current);

        if (!ImGui.Combo(id, ref index, names, names.Length))
        {
            return false;
        }

        value = Enum.Parse<T>(names[index]);
        return true;
    }

    private void DrawColorRow(Light light, ref bool dirty)
    {
        ImGui.TableNextRow();
        ImGui.TableSetColumnIndex(0);
        ImGui.Text("Color");

        ImGui.TableSetColumnIndex(1);
        ImGui.PushItemWidth(-1);

        Vector3 color = light.Color;
        if (PropertyDrawer.Color3("##Color", ref color, out bool activated, out bool deactivated))
        {
            light.Color = color;
            dirty = true;
        }
        _undo.Track(light, nameof(Light.Color), activated, deactivated);

        ImGui.PopItemWidth();

        DrawResetButton(light, nameof(Light.Color), Vector3.One, ref dirty);
    }

    private void DrawFloatRow(Light light, string propName, float speed, float min, float max, float defaultValue, ref bool dirty)
    {
        ImGui.TableNextRow();
        ImGui.TableSetColumnIndex(0);
        ImGui.Text(SplitCamelCase(propName));

        ImGui.TableSetColumnIndex(1);
        ImGui.PushItemWidth(-1);

        var property = typeof(Light).GetProperty(propName)!;
        float value = (float)property.GetValue(light)!;

        if (PropertyDrawer.Float($"##{propName}", ref value, speed, min, max))
        {
            property.SetValue(light, value);
            dirty = true;
        }
        _undo.Track(light, propName, ImGui.IsItemActivated(), ImGui.IsItemDeactivatedAfterEdit());

        ImGui.PopItemWidth();

        DrawResetButton(light, propName, defaultValue, ref dirty);
    }

    private void DrawResetButton(Light light, string propName, object defaultValue, ref bool dirty)
    {
        ImGui.TableSetColumnIndex(2);

        IntPtr icon = _textures.GetTexture("Reset");
        float size = ImGui.GetFrameHeight() - 4;

        if (ImGui.ImageButton($"Reset{propName}", icon, new Vector2(size, size)))
        {
            var property = typeof(Light).GetProperty(propName)!;
            var current = property.GetValue(light);

            if (!Equals(current, defaultValue))
            {
                _history.Execute(new ChangePropertyCommand(light, property, current!, defaultValue));
                property.SetValue(light, defaultValue);
                dirty = true;
            }
        }
    }

    private static string SplitCamelCase(string input)
    {
        return System.Text.RegularExpressions.Regex.Replace(input, "(?<!^)([A-Z])", " $1");
    }
}