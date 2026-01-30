using Cherris.Attributes;
using Cherris.Components;
using Cherris.Rendering;
using ImGuiNET;
using System.Numerics;
using System.Reflection;
using System.Runtime.InteropServices;

namespace CherrisEditor.Inspectors;

public sealed class TexturePropertyDrawer
{
    private readonly Editor _editor;
    private readonly EditorTextureManager _textureManager;

    public TexturePropertyDrawer(Editor editor, EditorTextureManager textureManager)
    {
        _editor = editor;
        _textureManager = textureManager;
    }

    public bool Draw(Component component, PropertyInfo prop, ITexture texture, DragDropTargetAttribute? dragDropAttr, out bool activated, out bool deactivated)
    {
        IntPtr handle = IntPtr.Zero;
        handle = texture?.GetBackendHandle() is int h && h != 0 ? h : _textureManager.GetTexture("File");

        ImGui.ImageButton($"tex_{prop.Name}", handle, new Vector2(64, 64), new Vector2(0, 1), new Vector2(1, 0));
        bool changed = false;
        activated = ImGui.IsItemActivated();
        deactivated = ImGui.IsItemDeactivatedAfterEdit();

        if (dragDropAttr != null && ImGui.BeginDragDropTarget())
        {
            var payload = ImGui.AcceptDragDropPayload(dragDropAttr.PayloadType);
            unsafe
            {
                if (payload.NativePtr != null)
                {
                    string? path = Marshal.PtrToStringAnsi(payload.Data);
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

        return changed || activated || deactivated;
    }
}