using Cherris;
using Cherris.Components;
using CherrisEditor.Undo;
using CherrisEditor.Undo.Commands;
using ImGuiNET;
using System.Numerics;
using System.Runtime.InteropServices;

namespace CherrisEditor.Inspectors;

[CustomInspector(typeof(MeshRenderer))]
public sealed class MeshRendererInspector : IComponentInspector
{
    private readonly Editor _editor;
    private readonly EditorTextureManager _textures;
    private readonly HistoryManager _history;
    private readonly UndoTracker _undo;

    public MeshRendererInspector(Editor editor, EditorTextureManager textures, HistoryManager history)
    {
        _editor = editor;
        _textures = textures;
        _history = history;
        _undo = new UndoTracker(history);
    }

    public unsafe bool Draw(Component component)
    {
        var renderer = (MeshRenderer)component;
        var material = renderer.Material;

        if (material == null)
        {
            ImGui.Text("No Material assigned.");
            return false;
        }

        bool dirty = false;

        if (!ImGui.BeginTable("MeshRendererTable", 3))
        {
            return false;
        }

        ImGui.TableSetupColumn("Property", ImGuiTableColumnFlags.WidthStretch, 0.475f);
        ImGui.TableSetupColumn("Value", ImGuiTableColumnFlags.WidthStretch, 0.475f);
        ImGui.TableSetupColumn("##Reset", ImGuiTableColumnFlags.WidthStretch, 0.05f);

        DrawTextureRow(material, ref dirty);
        DrawTilingRow(material, ref dirty);
        DrawEmissiveRow(material, ref dirty);

        ImGui.EndTable();
        return dirty;
    }

    private void DrawTextureRow(Material material, ref bool dirty)
    {
        ImGui.TableNextRow();
        ImGui.TableSetColumnIndex(0);
        ImGui.Text("Texture");

        ImGui.TableSetColumnIndex(1);
        DrawTextureThumb(material, ref dirty);

        ImGui.TableSetColumnIndex(2);
        DrawTextureResetButton(material, ref dirty);
    }

    private unsafe void DrawTextureThumb(Material material, ref bool dirty)
    {
        IntPtr handle = IntPtr.Zero;
        handle = material.Texture?.GetBackendHandle() is int h && h != 0 ? h : _textures.GetTexture("File");

        ImGui.ImageButton("TextureThumb", handle, new Vector2(64, 64), new Vector2(0, 1), new Vector2(1, 0));

        if (!ImGui.BeginDragDropTarget())
        {
            return;
        }

        var payload = ImGui.AcceptDragDropPayload("ASSET_PATH_TEXTURE");
        if (payload.NativePtr == null)
        {
            ImGui.EndDragDropTarget();
            return;
        }

        string path = Marshal.PtrToStringAnsi(payload.Data) ?? "";
        if (string.IsNullOrEmpty(path) || !File.Exists(path))
        {
            ImGui.EndDragDropTarget();
            return;
        }

        string newName = Path.GetFileNameWithoutExtension(path);
        var newTexture = _editor.ResourceManager.GetTexture(newName);

        _history.Execute(new ChangeMaterialTextureCommand(material, material.TextureName, material.Texture, newName, newTexture));
        dirty = true;

        ImGui.EndDragDropTarget();
    }

    private void DrawTextureResetButton(Material material, ref bool dirty)
    {
        IntPtr icon = _textures.GetTexture("Reset");
        float size = ImGui.GetFrameHeight() - 4;

        if (!ImGui.ImageButton("ResetTexture", icon, new Vector2(size, size)))
        {
            return;
        }

        var defaultTexture = _editor.ResourceManager.GetTexture("White");
        if (material.TextureName == "White")
        {
            return;
        }

        _history.Execute(new ChangeMaterialTextureCommand(material, material.TextureName, material.Texture, "White", defaultTexture));
        dirty = true;
    }

    private void DrawTilingRow(Material material, ref bool dirty)
    {
        ImGui.TableNextRow();
        ImGui.TableSetColumnIndex(0);
        ImGui.Text("Texture Tiling");

        ImGui.TableSetColumnIndex(1);
        ImGui.PushItemWidth(-1);

        var tiling = material.TextureTiling;
        if (PropertyDrawer.Vector2("##Tiling", ref tiling, out bool activated, out bool deactivated))
        {
            material.TextureTiling = tiling;
            dirty = true;
        }
        _undo.Track(material, nameof(Material.TextureTiling), activated, deactivated);

        ImGui.PopItemWidth();

        DrawPropertyResetButton(material, nameof(Material.TextureTiling), Vector2.One, ref dirty);
    }

    private void DrawEmissiveRow(Material material, ref bool dirty)
    {
        ImGui.TableNextRow();
        ImGui.TableSetColumnIndex(0);
        ImGui.Text("Emissive Color");

        ImGui.TableSetColumnIndex(1);
        ImGui.PushItemWidth(-1);

        var color = material.EmissiveColor;
        if (PropertyDrawer.Color3("##Emissive", ref color, out bool activated, out bool deactivated))
        {
            material.EmissiveColor = color;
            dirty = true;
        }
        _undo.Track(material, nameof(Material.EmissiveColor), activated, deactivated);

        ImGui.PopItemWidth();

        DrawPropertyResetButton(material, nameof(Material.EmissiveColor), Vector3.Zero, ref dirty);
    }

    private void DrawPropertyResetButton(Material material, string propName, object defaultValue, ref bool dirty)
    {
        ImGui.TableSetColumnIndex(2);

        IntPtr icon = _textures.GetTexture("Reset");
        float size = ImGui.GetFrameHeight() - 4;

        if (!ImGui.ImageButton($"Reset{propName}", icon, new Vector2(size, size)))
        {
            return;
        }

        var property = typeof(Material).GetProperty(propName)!;
        var current = property.GetValue(material)!;

        if (Equals(current, defaultValue))
        {
            return;
        }

        _history.Execute(new ChangePropertyCommand(material, property, current, defaultValue));
        property.SetValue(material, defaultValue);
        dirty = true;
    }
}