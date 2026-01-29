using ImGuiNET;
using System.Numerics;

namespace CherrisEditor.Inspectors;

public static class PropertyDrawer
{
    public static bool Float(string id, ref float value, float speed = 0.01f, float min = float.MinValue, float max = float.MaxValue)
    {
        return ImGui.DragFloat(id, ref value, speed, min, max);
    }

    public static bool Int(string id, ref int value)
    {
        return ImGui.DragInt(id, ref value);
    }

    public static bool Bool(string id, ref bool value)
    {
        return ImGui.Checkbox(id, ref value);
    }

    public static bool String(string id, ref string value, uint maxLength = 256)
    {
        return ImGui.InputText(id, ref value, maxLength);
    }

    public static bool EnumField<T>(string id, ref T value) where T : struct, Enum
    {
        var names = Enum.GetNames<T>();
        var current = value.ToString();
        var index = Array.IndexOf(names, current);

        if (!ImGui.Combo(id, ref index, names, names.Length))
        {
            return false;
        }

        value = Enum.Parse<T>(names[index]);
        return true;
    }

    public static bool Vector2(string id, ref Vector2 value, out bool activated, out bool deactivated)
    {
        activated = false;
        deactivated = false;

        ImGui.PushID(id);
        var style = ImGui.GetStyle();
        float itemWidth = (ImGui.GetContentRegionAvail().X - (style.ItemSpacing.X * 3) - ImGui.CalcTextSize("X").X - ImGui.CalcTextSize("Y").X) / 2f;

        bool changed = false;

        ImGui.AlignTextToFramePadding();
        ImGui.PushStyleColor(ImGuiCol.Text, new Vector4(0.8f, 0.2f, 0.2f, 1));
        ImGui.Text("X");
        ImGui.PopStyleColor();
        ImGui.SameLine();
        ImGui.PushItemWidth(itemWidth);

        if (ImGui.DragFloat("##X", ref value.X, 0.1f))
        {
            changed = true;
        }

        activated |= ImGui.IsItemActivated();
        deactivated |= ImGui.IsItemDeactivatedAfterEdit();
        ImGui.PopItemWidth();
        ImGui.SameLine();

        ImGui.AlignTextToFramePadding();
        ImGui.PushStyleColor(ImGuiCol.Text, new Vector4(0.2f, 0.8f, 0.2f, 1));
        ImGui.Text("Y");
        ImGui.PopStyleColor();
        ImGui.SameLine();
        ImGui.PushItemWidth(itemWidth);

        if (ImGui.DragFloat("##Y", ref value.Y, 0.1f))
        {
            changed = true;
        }

        activated |= ImGui.IsItemActivated();
        deactivated |= ImGui.IsItemDeactivatedAfterEdit();
        ImGui.PopItemWidth();

        ImGui.PopID();
        return changed;
    }

    public static bool Vector3(string id, ref Vector3 value, out bool activated, out bool deactivated)
    {
        activated = false;
        deactivated = false;

        ImGui.PushID(id);
        var style = ImGui.GetStyle();
        float itemWidth = (ImGui.GetContentRegionAvail().X - (style.ItemSpacing.X * 5) - (ImGui.CalcTextSize("X").X * 3)) / 3f;

        bool changed = false;
        string[] labels = { "X", "Y", "Z" };
        Vector4[] colors =
        {
            new(0.8f, 0.2f, 0.2f, 1),
            new(0.2f, 0.8f, 0.2f, 1),
            new(0.2f, 0.3f, 0.8f, 1)
        };
        float[] components = { value.X, value.Y, value.Z };

        for (int i = 0; i < 3; i++)
        {
            if (i > 0)
            {
                ImGui.SameLine();
            }

            ImGui.AlignTextToFramePadding();
            ImGui.PushStyleColor(ImGuiCol.Text, colors[i]);
            ImGui.Text(labels[i]);
            ImGui.PopStyleColor();
            ImGui.SameLine();
            ImGui.PushItemWidth(itemWidth);

            if (ImGui.DragFloat($"##{labels[i]}", ref components[i], 0.1f))
            {
                changed = true;
            }

            activated |= ImGui.IsItemActivated();
            deactivated |= ImGui.IsItemDeactivatedAfterEdit();
            ImGui.PopItemWidth();
        }

        value = new Vector3(components[0], components[1], components[2]);
        ImGui.PopID();
        return changed;
    }

    public static bool Color3(string id, ref Vector3 color, out bool activated, out bool deactivated)
    {
        ImGui.PushID(id);
        bool changed = ImGui.ColorEdit3(id, ref color, ImGuiColorEditFlags.Float | ImGuiColorEditFlags.HDR);
        activated = ImGui.IsItemActivated();
        deactivated = ImGui.IsItemDeactivatedAfterEdit();
        ImGui.PopID();
        return changed;
    }

    public static bool Color4(string id, ref Vector4 color, out bool activated, out bool deactivated)
    {
        ImGui.PushID(id);
        bool changed = ImGui.ColorEdit4(id, ref color, ImGuiColorEditFlags.Float | ImGuiColorEditFlags.HDR);
        activated = ImGui.IsItemActivated();
        deactivated = ImGui.IsItemDeactivatedAfterEdit();
        ImGui.PopID();
        return changed;
    }
}