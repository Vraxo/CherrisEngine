using ImGuiNET;
using System.Numerics;
using System.Text.RegularExpressions;

namespace CherrisEditor.UI;

public static class ImGuiPropertyControls
{
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
        ImGui.PushStyleColor(ImGuiCol.Text, new Vector4(0.8f, 0.2f, 0.2f, 1.0f));
        ImGui.Text("X");
        ImGui.PopStyleColor();
        ImGui.SameLine();

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

        ImGui.PopItemWidth();
        ImGui.SameLine();

        // Y
        ImGui.AlignTextToFramePadding();
        ImGui.PushStyleColor(ImGuiCol.Text, new Vector4(0.2f, 0.8f, 0.2f, 1.0f));
        ImGui.Text("Y");
        ImGui.PopStyleColor();
        ImGui.SameLine();

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

        // X
        ImGui.AlignTextToFramePadding();
        ImGui.PushStyleColor(ImGuiCol.Text, new Vector4(0.8f, 0.2f, 0.2f, 1.0f));
        ImGui.Text("X");
        ImGui.PopStyleColor();
        ImGui.SameLine();

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

        ImGui.PopItemWidth();
        ImGui.SameLine();

        // Y
        ImGui.AlignTextToFramePadding();
        ImGui.PushStyleColor(ImGuiCol.Text, new Vector4(0.2f, 0.8f, 0.2f, 1.0f));
        ImGui.Text("Y");
        ImGui.PopStyleColor();
        ImGui.SameLine();

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
        ImGui.SameLine();

        // Z
        ImGui.AlignTextToFramePadding();
        ImGui.PushStyleColor(ImGuiCol.Text, new Vector4(0.2f, 0.3f, 0.8f, 1.0f));
        ImGui.Text("Z");
        ImGui.PopStyleColor();
        ImGui.SameLine();

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
        ImGui.PushID(label);

        if (ImGui.IsItemActivated())
        {
            activated = true;
        }

        bool changed = ImGui.ColorEdit3(label, ref color, ImGuiColorEditFlags.Float | ImGuiColorEditFlags.HDR);
        if (ImGui.IsItemDeactivatedAfterEdit())
        {
            deactivated = true;
        }

        ImGui.PopID();
        activated = false;
        deactivated = false;
        return changed;
    }

    public static bool DrawColor4Control(string label, ref Vector4 color, out bool activated, out bool deactivated)
    {
        ImGui.PushID(label);

        if (ImGui.IsItemActivated())
        {
            activated = true;
        }

        bool changed = ImGui.ColorEdit4(label, ref color, ImGuiColorEditFlags.Float | ImGuiColorEditFlags.HDR);
        if (ImGui.IsItemDeactivatedAfterEdit())
        {
            deactivated = true;
        }

        ImGui.PopID();
        activated = false;
        deactivated = false;
        return changed;
    }

    public static string SplitPascalCase(string input)
    {
        return Regex.Replace(input, "(?<!^)([A-Z])", " $1");
    }
}