using ImGuiNET;
using System.Numerics;
using System.Runtime.InteropServices;

namespace CherrisEditor.Inspectors;

public static class PropertyDrawingPrimitives
{
    public static bool Float(string id, float value, float speed, float min, float max, out float newValue, out bool activated, out bool deactivated)
    {
        ImGui.PushID(id);
        newValue = value;
        bool changed = ImGui.DragFloat("##val", ref newValue, speed, min, max);
        activated = ImGui.IsItemActivated();
        deactivated = ImGui.IsItemDeactivatedAfterEdit();
        ImGui.PopID();
        return changed;
    }

    public static bool SliderFloat(string id, float value, float min, float max, out float newValue, out bool activated, out bool deactivated)
    {
        ImGui.PushID(id);
        newValue = value;
        bool changed = ImGui.SliderFloat("##val", ref newValue, min, max);
        activated = ImGui.IsItemActivated();
        deactivated = ImGui.IsItemDeactivatedAfterEdit();
        ImGui.PopID();
        return changed;
    }

    public static bool Int(string id, int value, out int newValue, out bool activated, out bool deactivated)
    {
        ImGui.PushID(id);
        newValue = value;
        bool changed = ImGui.DragInt("##val", ref newValue);
        activated = ImGui.IsItemActivated();
        deactivated = ImGui.IsItemDeactivatedAfterEdit();
        ImGui.PopID();
        return changed;
    }

    public static bool Bool(string id, bool value, out bool newValue, out bool activated, out bool deactivated)
    {
        ImGui.PushID(id);
        newValue = value;
        bool changed = ImGui.Checkbox("##val", ref newValue);
        activated = ImGui.IsItemActivated();
        deactivated = ImGui.IsItemDeactivatedAfterEdit();
        ImGui.PopID();
        return changed;
    }

    public static bool String(string id, string value, out string newValue, out bool activated, out bool deactivated)
    {
        ImGui.PushID(id);
        newValue = value ?? "";
        bool changed = ImGui.InputText("##val", ref newValue, 256);
        activated = ImGui.IsItemActivated();
        deactivated = ImGui.IsItemDeactivatedAfterEdit();
        ImGui.PopID();
        return changed;
    }

    public static bool DragDropString(string id, string value, string payloadType, out string newValue, out bool activated, out bool deactivated)
    {
        ImGui.PushID(id);
        newValue = value ?? "";
        bool changed = ImGui.InputText("##val", ref newValue, 256);

        if (ImGui.BeginDragDropTarget())
        {
            var payload = ImGui.AcceptDragDropPayload(payloadType);
            unsafe
            {
                if (payload.NativePtr != null)
                {
                    string? path = Marshal.PtrToStringAnsi(payload.Data);
                    if (!string.IsNullOrEmpty(path))
                    {
                        newValue = Path.GetFileNameWithoutExtension(path);
                        changed = true;
                    }
                }
            }
            ImGui.EndDragDropTarget();
        }

        activated = ImGui.IsItemActivated();
        deactivated = ImGui.IsItemDeactivatedAfterEdit();
        ImGui.PopID();
        return changed;
    }

    public static bool EnumField(string id, Enum value, out Enum newValue, out bool activated, out bool deactivated)
    {
        ImGui.PushID(id);
        newValue = value;
        var names = Enum.GetNames(value.GetType());
        string currentName = value.ToString();
        int index = Array.IndexOf(names, currentName);

        bool changed = ImGui.Combo("##val", ref index, names, names.Length);
        if (changed)
        {
            newValue = (Enum)Enum.Parse(value.GetType(), names[index]);
        }

        activated = ImGui.IsItemActivated();
        deactivated = ImGui.IsItemDeactivatedAfterEdit();
        ImGui.PopID();
        return changed;
    }

    public static bool Vector2Field(string id, Vector2 value, out Vector2 newValue, out bool activated, out bool deactivated)
    {
        activated = false;
        deactivated = false;
        ImGui.PushID(id);

        var style = ImGui.GetStyle();
        float itemWidth = (ImGui.GetContentRegionAvail().X - (style.ItemSpacing.X * 3) - ImGui.CalcTextSize("X").X - ImGui.CalcTextSize("Y").X) / 2f;
        float[] comps = { value.X, value.Y };
        bool changed = false;

        for (int i = 0; i < 2; i++)
        {
            if (i > 0)
            {
                ImGui.SameLine();
            }

            ImGui.AlignTextToFramePadding();
            ImGui.Text(i == 0 ? "X" : "Y");
            ImGui.SameLine();
            ImGui.PushItemWidth(itemWidth);

            if (ImGui.DragFloat($"##c{i}", ref comps[i], 0.1f))
            {
                changed = true;
            }

            activated |= ImGui.IsItemActivated();
            deactivated |= ImGui.IsItemDeactivatedAfterEdit();
            ImGui.PopItemWidth();
        }

        newValue = new Vector2(comps[0], comps[1]);
        ImGui.PopID();
        return changed;
    }

    public static bool Vector3Field(string id, Vector3 value, out Vector3 newValue, out bool activated, out bool deactivated)
    {
        activated = false;
        deactivated = false;
        ImGui.PushID(id);

        var style = ImGui.GetStyle();
        float itemWidth = (ImGui.GetContentRegionAvail().X - (style.ItemSpacing.X * 5) - (ImGui.CalcTextSize("X").X * 3)) / 3f;
        float[] comps = { value.X, value.Y, value.Z };
        string[] labels = { "X", "Y", "Z" };
        bool changed = false;

        for (int i = 0; i < 3; i++)
        {
            if (i > 0)
            {
                ImGui.SameLine();
            }

            ImGui.AlignTextToFramePadding();
            ImGui.Text(labels[i]);
            ImGui.SameLine();
            ImGui.PushItemWidth(itemWidth);

            if (ImGui.DragFloat($"##c{i}", ref comps[i], 0.1f))
            {
                changed = true;
            }

            activated |= ImGui.IsItemActivated();
            deactivated |= ImGui.IsItemDeactivatedAfterEdit();
            ImGui.PopItemWidth();
        }

        newValue = new Vector3(comps[0], comps[1], comps[2]);
        ImGui.PopID();
        return changed;
    }

    public static bool Color3(string id, Vector3 value, out Vector3 newValue, out bool activated, out bool deactivated)
    {
        ImGui.PushID(id);
        newValue = value;
        bool changed = ImGui.ColorEdit3("##val", ref newValue, ImGuiColorEditFlags.Float | ImGuiColorEditFlags.HDR);
        activated = ImGui.IsItemActivated();
        deactivated = ImGui.IsItemDeactivatedAfterEdit();
        ImGui.PopID();
        return changed;
    }

    public static object CreateDefault(Type type)
    {
        if (type == typeof(string))
        {
            return "";
        }

        if (type == typeof(Vector2))
        {
            return Vector2.Zero;
        }

        if (type == typeof(Vector3))
        {
            return Vector3.Zero;
        }

        return type == typeof(Vector4) ? Vector4.Zero : Activator.CreateInstance(type) ?? new object();
    }
}