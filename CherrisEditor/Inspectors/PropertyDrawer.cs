using Cherris.Attributes;
using Cherris.Components;
using Cherris.Core;
using Cherris.Rendering;
using CherrisEditor.Undo;
using ImGuiNET;
using System.Numerics;
using System.Reflection;
using System.Runtime.InteropServices;

namespace CherrisEditor.Inspectors;

public sealed class PropertyDrawer
{
    private readonly Editor _editor;
    private readonly EditorTextureManager _textures;
    private readonly HistoryManager _history;
    private readonly UndoTracker _undo;

    public PropertyDrawer(Editor editor, EditorTextureManager textures, HistoryManager history)
    {
        _editor = editor;
        _textures = textures;
        _history = history;
        _undo = new UndoTracker(history);
    }

    public bool Draw(Component component, PropertyInfo prop, out bool activated, out bool deactivated)
    {
        activated = false;
        deactivated = false;

        object value = prop.GetValue(component) ?? CreateDefault(prop.PropertyType);
        ImGui.PushID(prop.Name);

        bool changed = false;
        var rangeAttr = prop.GetCustomAttribute<RangeAttribute>();
        var colorAttr = prop.GetCustomAttribute<ColorUsageAttribute>();
        var dragDropAttr = prop.GetCustomAttribute<DragDropTargetAttribute>();

        if (prop.PropertyType == typeof(float))
        {
            float v = (float)value;
            float min = rangeAttr?.Min ?? float.MinValue;
            float max = rangeAttr?.Max ?? float.MaxValue;
            float speed = rangeAttr?.Speed ?? 0.01f;

            changed = rangeAttr != null ? ImGui.SliderFloat("##val", ref v, min, max) : ImGui.DragFloat("##val", ref v, speed, min, max);

            if (changed)
            {
                prop.SetValue(component, v);
            }

            activated = ImGui.IsItemActivated();
            deactivated = ImGui.IsItemDeactivatedAfterEdit();
        }
        else if (prop.PropertyType == typeof(int))
        {
            int v = (int)value;
            changed = ImGui.DragInt("##val", ref v);
            if (changed)
            {
                prop.SetValue(component, v);
            }

            activated = ImGui.IsItemActivated();
            deactivated = ImGui.IsItemDeactivatedAfterEdit();
        }
        else if (prop.PropertyType == typeof(bool))
        {
            bool v = (bool)value;
            changed = ImGui.Checkbox("##val", ref v);
            if (changed)
            {
                prop.SetValue(component, v);
            }

            activated = ImGui.IsItemActivated();
            deactivated = ImGui.IsItemDeactivatedAfterEdit();
        }
        else if (prop.PropertyType == typeof(string))
        {
            string v = (string)value ?? "";

            if (dragDropAttr != null)
            {
                changed = DrawDragDropString("##val", ref v, dragDropAttr.PayloadType);
                if (changed)
                {
                    prop.SetValue(component, v);
                    UpdateResourceProperty(component, prop.Name, v);
                }
            }
            else
            {
                changed = ImGui.InputText("##val", ref v, 256);
                if (changed)
                {
                    prop.SetValue(component, v);
                }
            }
            activated = ImGui.IsItemActivated();
            deactivated = ImGui.IsItemDeactivatedAfterEdit();
        }
        else if (prop.PropertyType.IsEnum)
        {
            var enumValue = (Enum)value;
            if (DrawEnum("##val", enumValue, out var newValue))
            {
                prop.SetValue(component, newValue);
                changed = true;
            }
            activated = ImGui.IsItemActivated();
            deactivated = ImGui.IsItemDeactivatedAfterEdit();
        }
        else if (prop.PropertyType == typeof(Vector2))
        {
            var v = (Vector2)value;
            changed = DrawVector2("##val", ref v, out activated, out deactivated);
            if (changed)
            {
                prop.SetValue(component, v);
            }
        }
        else if (prop.PropertyType == typeof(Vector3))
        {
            var v = (Vector3)value;
            changed = colorAttr != null
                ? DrawColor3("##val", ref v, out activated, out deactivated)
                : DrawVector3("##val", ref v, out activated, out deactivated);
            if (changed)
            {
                prop.SetValue(component, v);
            }
        }
        else if (prop.PropertyType == typeof(ITexture))
        {
            changed = DrawTextureProperty(component, prop, (ITexture)value, dragDropAttr);
            activated = ImGui.IsItemActivated();
            deactivated = ImGui.IsItemDeactivatedAfterEdit();
        }
        else
        {
            ImGui.Text(value.ToString() ?? "null");
        }

        _undo.Track(component, prop.Name, activated, deactivated);
        ImGui.PopID();
        return changed;
    }

    private void UpdateResourceProperty(Component component, string propertyName, string resourceName)
    {
        if (string.IsNullOrEmpty(resourceName) || !propertyName.EndsWith("Name"))
        {
            return;
        }

        string targetPropertyName = propertyName[..^4];
        var targetProp = component.GetType().GetProperty(targetPropertyName);

        if (targetProp == null)
        {
            return;
        }

        if (targetProp.PropertyType == typeof(ITexture))
        {
            var texture = _editor.ResourceManager.GetTexture(resourceName);
            if (texture != null)
            {
                targetProp.SetValue(component, texture);
            }
        }
        else if (targetProp.PropertyType == typeof(AudioClip))
        {
            var clip = _editor.ResourceManager.GetAudioClip(resourceName);
            if (clip != null)
            {
                targetProp.SetValue(component, clip);
            }
        }
        else if (targetProp.PropertyType == typeof(Mesh))
        {
            var mesh = _editor.ResourceManager.GetMesh(resourceName);
            if (mesh != null)
            {
                targetProp.SetValue(component, mesh);
            }
        }
    }

    private bool DrawDragDropString(string id, ref string value, string payloadType)
    {
        bool changed = ImGui.InputText(id, ref value, 256);

        if (ImGui.BeginDragDropTarget())
        {
            var payload = ImGui.AcceptDragDropPayload(payloadType);
            unsafe
            {
                if (payload.NativePtr != null)
                {
                    string path = Marshal.PtrToStringAnsi(payload.Data) ?? "";
                    if (!string.IsNullOrEmpty(path))
                    {
                        value = Path.GetFileNameWithoutExtension(path);
                        changed = true;
                    }
                }
            }
            ImGui.EndDragDropTarget();
        }

        return changed;
    }

    private bool DrawTextureProperty(Component component, PropertyInfo prop, ITexture texture, DragDropTargetAttribute? dragDropAttr)
    {
        IntPtr handle = IntPtr.Zero;
        handle = texture?.GetBackendHandle() is int h && h != 0 ? h : _textures.GetTexture("File");

        ImGui.ImageButton($"tex_{prop.Name}", handle, new Vector2(64, 64), new Vector2(0, 1), new Vector2(1, 0));
        bool changed = false;

        if (dragDropAttr != null && ImGui.BeginDragDropTarget())
        {
            var payload = ImGui.AcceptDragDropPayload(dragDropAttr.PayloadType);
            unsafe
            {
                if (payload.NativePtr != null)
                {
                    string path = Marshal.PtrToStringAnsi(payload.Data) ?? "";
                    if (!string.IsNullOrEmpty(path))
                    {
                        string name = Path.GetFileNameWithoutExtension(path);
                        var newTex = _editor.ResourceManager.GetTexture(name);
                        if (newTex != null)
                        {
                            prop.SetValue(component, newTex);

                            var textureNameProp = component.GetType().GetProperty(prop.Name + "Name");
                            if (textureNameProp != null && textureNameProp.PropertyType == typeof(string))
                            {
                                textureNameProp.SetValue(component, name);
                            }

                            changed = true;
                        }
                    }
                }
            }
            ImGui.EndDragDropTarget();
        }

        return changed;
    }

    private static bool DrawEnum(string id, Enum current, out Enum result)
    {
        result = current;
        var names = Enum.GetNames(current.GetType());
        string currentName = current.ToString();
        int index = Array.IndexOf(names, currentName);

        if (!ImGui.Combo(id, ref index, names, names.Length))
        {
            return false;
        }

        result = (Enum)Enum.Parse(current.GetType(), names[index]);
        return true;
    }

    private static bool DrawVector2(string id, ref Vector2 value, out bool activated, out bool deactivated)
    {
        activated = false;
        deactivated = false;

        ImGui.PushID(id);
        var style = ImGui.GetStyle();
        float itemWidth = (ImGui.GetContentRegionAvail().X - (style.ItemSpacing.X * 3) - ImGui.CalcTextSize("X").X - ImGui.CalcTextSize("Y").X) / 2f;
        bool changed = false;
        float[] components = { value.X, value.Y };

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

            if (ImGui.DragFloat($"##c{i}", ref components[i], 0.1f))
            {
                changed = true;
            }

            activated |= ImGui.IsItemActivated();
            deactivated |= ImGui.IsItemDeactivatedAfterEdit();
            ImGui.PopItemWidth();
        }

        value = new Vector2(components[0], components[1]);
        ImGui.PopID();
        return changed;
    }

    private static bool DrawVector3(string id, ref Vector3 value, out bool activated, out bool deactivated)
    {
        activated = false;
        deactivated = false;

        ImGui.PushID(id);
        var style = ImGui.GetStyle();
        float itemWidth = (ImGui.GetContentRegionAvail().X - (style.ItemSpacing.X * 5) - (ImGui.CalcTextSize("X").X * 3)) / 3f;
        bool changed = false;
        float[] components = { value.X, value.Y, value.Z };
        string[] labels = { "X", "Y", "Z" };

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

            if (ImGui.DragFloat($"##c{i}", ref components[i], 0.1f))
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

    private static bool DrawColor3(string id, ref Vector3 value, out bool activated, out bool deactivated)
    {
        ImGui.PushID(id);
        bool changed = ImGui.ColorEdit3(id, ref value, ImGuiColorEditFlags.Float | ImGuiColorEditFlags.HDR);
        activated = ImGui.IsItemActivated();
        deactivated = ImGui.IsItemDeactivatedAfterEdit();
        ImGui.PopID();
        return changed;
    }

    private static object CreateDefault(Type type)
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