using Cherris.Attributes;
using Cherris.Components;
using Cherris.Core;
using Cherris.Rendering;
using CherrisEditor.Undo;
using ImGuiNET;
using System.Numerics;
using System.Reflection;

namespace CherrisEditor.Inspectors;

public sealed class PropertyDrawer
{
    private readonly Editor _editor;
    private readonly EditorTextureManager _textures;
    private readonly HistoryManager _history;

    public PropertyDrawer(Editor editor, EditorTextureManager textures, HistoryManager history)
    {
        _editor = editor;
        _textures = textures;
        _history = history;
    }

    public void Draw(Component component, PropertyInfo prop)
    {
        object currentValue = prop.GetValue(component) ?? CreateDefault(prop.PropertyType);
        bool changed = false;

        ImGui.PushID(prop.Name);

        if (prop.PropertyType == typeof(float))
        {
            changed = DrawFloat(component, prop, (float)currentValue);
        }
        else if (prop.PropertyType == typeof(int))
        {
            changed = DrawInt(component, prop, (int)currentValue);
        }
        else if (prop.PropertyType == typeof(bool))
        {
            changed = DrawBool(component, prop, (bool)currentValue);
        }
        else if (prop.PropertyType == typeof(string))
        {
            changed = DrawString(component, prop, (string)currentValue);
        }
        else if (prop.PropertyType.IsEnum)
        {
            changed = DrawEnum(component, prop, (Enum)currentValue);
        }
        else if (prop.PropertyType == typeof(Vector2))
        {
            changed = DrawVector2(component, prop, (Vector2)currentValue);
        }
        else if (prop.PropertyType == typeof(Vector3))
        {
            changed = DrawVector3(component, prop, (Vector3)currentValue);
        }
        else if (prop.PropertyType == typeof(ITexture))
        {
            changed = DrawTexture(component, prop, (ITexture)currentValue);
        }

        ImGui.PopID();

        if (changed && ImGui.IsItemDeactivatedAfterEdit())
        {
            RecordUndo(component, prop, currentValue);
        }
    }

    private bool DrawFloat(Component component, PropertyInfo prop, float value)
    {
        var range = prop.GetCustomAttribute<RangeAttribute>();
        float newValue = value;

        bool changed = range != null
            ? ImGui.SliderFloat("##val", ref newValue, range.Min, range.Max)
            : ImGui.DragFloat("##val", ref newValue, 0.01f);

        if (changed)
        {
            prop.SetValue(component, newValue);
        }

        return changed;
    }

    private bool DrawInt(Component component, PropertyInfo prop, int value)
    {
        int newValue = value;
        bool changed = ImGui.DragInt("##val", ref newValue);
        if (changed)
        {
            prop.SetValue(component, newValue);
        }

        return changed;
    }

    private bool DrawBool(Component component, PropertyInfo prop, bool value)
    {
        bool newValue = value;
        bool changed = ImGui.Checkbox("##val", ref newValue);
        if (changed)
        {
            prop.SetValue(component, newValue);
        }

        return changed;
    }

    private bool DrawString(Component component, PropertyInfo prop, string value)
    {
        string newValue = value ?? "";
        bool changed = ImGui.InputText("##val", ref newValue, 256);

        if (changed)
        {
            prop.SetValue(component, newValue);

            if (prop.GetCustomAttribute<DragDropTargetAttribute>() is not null
                && prop.Name.EndsWith("Name"))
            {
                SyncResource(component, prop.Name, newValue);
            }
        }

        DrawDragDropTarget(component, prop);
        return changed;
    }

    private bool DrawEnum(Component component, PropertyInfo prop, Enum value)
    {
        var names = Enum.GetNames(value.GetType());
        int index = Array.IndexOf(names, value.ToString());

        bool changed = ImGui.Combo("##val", ref index, names, names.Length);
        if (changed)
        {
            prop.SetValue(component, Enum.Parse(value.GetType(), names[index]));
        }
        return changed;
    }

    private bool DrawVector2(Component component, PropertyInfo prop, Vector2 value)
    {
        System.Numerics.Vector2 vec = value;
        bool changed = ImGui.DragFloat2("##val", ref vec, 0.1f);
        if (changed)
        {
            prop.SetValue(component, vec);
        }

        return changed;
    }

    private bool DrawVector3(Component component, PropertyInfo prop, Vector3 value)
    {
        bool isColor = prop.GetCustomAttribute<ColorUsageAttribute>() is not null;
        System.Numerics.Vector3 vec = value;

        bool changed = isColor
            ? ImGui.ColorEdit3("##val", ref vec, ImGuiColorEditFlags.Float | ImGuiColorEditFlags.HDR)
            : ImGui.DragFloat3("##val", ref vec, 0.1f);

        if (changed)
        {
            prop.SetValue(component, vec);
        }

        return changed;
    }

    private bool DrawTexture(Component component, PropertyInfo prop, ITexture texture)
    {
        IntPtr handle = ResolveTextureHandle(texture);
        bool changed = false;

        if (ImGui.ImageButton($"tex_{prop.Name}", handle, new(64, 64), new(0, 1), new(1, 0)))
        {
        }

        if (ImGui.BeginDragDropTarget())
        {
            var payload = ImGui.AcceptDragDropPayload("ASSET_PATH_TEXTURE");
            unsafe
            {
                if (payload.NativePtr != null)
                {
                    string path = System.Runtime.InteropServices.Marshal.PtrToStringAnsi(payload.Data) ?? "";
                    if (!string.IsNullOrEmpty(path))
                    {
                        string textureName = Path.GetFileNameWithoutExtension(path);
                        var newTexture = _editor.ResourceManager.GetTexture(textureName);
                        if (newTexture is not null)
                        {
                            prop.SetValue(component, newTexture);
                            var nameProp = component.GetType().GetProperty($"{prop.Name}Name");
                            nameProp?.SetValue(component, textureName);
                            changed = true;
                        }
                    }
                }
            }
            ImGui.EndDragDropTarget();
        }

        return changed;
    }

    private IntPtr ResolveTextureHandle(ITexture? texture)
    {
        if (texture?.GetBackendHandle() is int handle && handle != 0)
        {
            return handle;
        }

        return _textures.GetTexture("File");
    }

    private void DrawDragDropTarget(Component component, PropertyInfo prop)
    {
        var attr = prop.GetCustomAttribute<DragDropTargetAttribute>();
        if (attr is null || !ImGui.BeginDragDropTarget())
        {
            return;
        }

        var payload = ImGui.AcceptDragDropPayload(attr.PayloadType);
        unsafe
        {
            if (payload.NativePtr != null)
            {
                string path = System.Runtime.InteropServices.Marshal.PtrToStringAnsi(payload.Data) ?? "";
                if (!string.IsNullOrEmpty(path))
                {
                    string value = Path.GetFileNameWithoutExtension(path);
                    prop.SetValue(component, value);
                    SyncResource(component, prop.Name, value);
                }
            }
        }
        ImGui.EndDragDropTarget();
    }

    private void SyncResource(Component component, string nameProperty, string resourceName)
    {
        if (!nameProperty.EndsWith("Name"))
        {
            return;
        }

        string targetName = nameProperty[..^4];
        var targetProp = component.GetType().GetProperty(targetName);
        if (targetProp is null)
        {
            return;
        }

        object? resource = targetProp.PropertyType switch
        {
            Type t when t == typeof(ITexture) => _editor.ResourceManager.GetTexture(resourceName),
            Type t when t == typeof(AudioClip) => _editor.ResourceManager.GetAudioClip(resourceName),
            Type t when t == typeof(Mesh) => _editor.ResourceManager.GetMesh(resourceName),
            _ => null
        };

        if (resource is not null)
        {
            targetProp.SetValue(component, resource);
        }
    }

    private void RecordUndo(Component component, PropertyInfo prop, object oldValue)
    {
        object? newValue = prop.GetValue(component);
        if (newValue is not null && !Equals(oldValue, newValue))
        {
            _history.Execute(new Undo.Commands.ChangePropertyCommand(component, prop, oldValue, newValue));
        }
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

        return Activator.CreateInstance(type) ?? new object();
    }
}