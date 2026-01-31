using Cherris.Attributes;
using Cherris.Components;
using Cherris.Rendering;
using ImGuiNET;
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
        IntPtr handle = ResolveTextureHandle(texture);

        ImGui.ImageButton($"tex_{prop.Name}", handle, new(64, 64), new(0, 1), new(1, 0));

        activated = ImGui.IsItemActivated();
        deactivated = ImGui.IsItemDeactivatedAfterEdit();

        bool changed = ProcessDragDropTarget(component, prop, dragDropAttr);

        return changed || activated || deactivated;
    }

    private IntPtr ResolveTextureHandle(ITexture? texture)
    {
        if (texture?.GetBackendHandle() is int handle && handle != 0)
        {
            return handle;
        }

        return _textureManager.GetTexture("File");
    }

    private bool ProcessDragDropTarget(Component component, PropertyInfo prop, DragDropTargetAttribute? dragDropAttr)
    {
        if (dragDropAttr is null || !ImGui.BeginDragDropTarget())
        {
            return false;
        }

        string? textureName = AcceptDragDropPayload(dragDropAttr);
        ImGui.EndDragDropTarget();

        if (string.IsNullOrEmpty(textureName))
        {
            return false;
        }

        ITexture? newTexture = _editor.ResourceManager.GetTexture(textureName);

        if (newTexture is null)
        {
            return false;
        }

        AssignTextureAndSyncName(component, prop, newTexture, textureName);
        return true;
    }

    private unsafe string? AcceptDragDropPayload(DragDropTargetAttribute dragDropAttr)
    {
        ImGuiPayloadPtr payload = ImGui.AcceptDragDropPayload(dragDropAttr.PayloadType);

        if (payload.NativePtr == null)
        {
            return null;
        }

        string? path = Marshal.PtrToStringAnsi(payload.Data);
        return string.IsNullOrEmpty(path) ? null : Path.GetFileNameWithoutExtension(path);
    }

    private static void AssignTextureAndSyncName(Component component, PropertyInfo prop, ITexture newTexture, string textureName)
    {
        prop.SetValue(component, newTexture);

        PropertyInfo? nameProp = component.GetType().GetProperty($"{prop.Name}Name");

        if ((nameProp?.PropertyType) != typeof(string))
        {
            return;
        }

        nameProp.SetValue(component, textureName);
    }
}