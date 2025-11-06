using Cherris;
using Cherris.Components;
using CherrisEditor.Undo;
using CherrisEditor.Undo.Commands;
using ImGuiNET;
using System.Numerics;
using System.Runtime.InteropServices;

namespace CherrisEditor.Inspectors;

[CustomInspector(typeof(MeshRenderer))]
public class MeshRendererInspector : IComponentInspector
{
    private readonly Editor _editor;
    private readonly EditorTextureManager _textureManager;
    private readonly HistoryManager _history;
    private object _undoInitialValue;

    public MeshRendererInspector(Editor editor, EditorTextureManager textureManager, HistoryManager history)
    {
        _editor = editor;
        _textureManager = textureManager;
        _history = history;
    }

    public unsafe bool Draw(Component component)
    {
        var mr = (MeshRenderer)component;
        var material = mr.Material;
        bool dirty = false;

        if (material is null)
        {
            ImGui.Text("No Material assigned.");
            return false;
        }

        if (!ImGui.BeginTable("MRTable", 3)) return false;
        ImGui.TableSetupColumn("Property", ImGuiTableColumnFlags.WidthStretch, 0.475f);
        ImGui.TableSetupColumn("Value", ImGuiTableColumnFlags.WidthStretch, 0.475f);
        ImGui.TableSetupColumn("##Reset", ImGuiTableColumnFlags.WidthStretch, 0.05f);

        IntPtr resetIcon = _textureManager.GetTexture("Reset");
        float buttonSize = ImGui.GetFrameHeight() - 4;

        // Texture
        ImGui.TableNextRow();
        ImGui.TableSetColumnIndex(0);
        ImGui.Text("Texture");
        ImGui.TableSetColumnIndex(1);

        IntPtr textureHandle = IntPtr.Zero;
        if (material.Texture?.GetBackendHandle() is int handle && handle != 0)
        {
            textureHandle = (IntPtr)handle;
        }
        else
        {
            textureHandle = _textureManager.GetTexture("File");
        }

        ImGui.ImageButton("TextureThumb", textureHandle, new Vector2(64, 64), new Vector2(0, 1), new Vector2(1, 0));

        if (ImGui.BeginDragDropTarget())
        {
            ImGuiPayloadPtr payload = ImGui.AcceptDragDropPayload("ASSET_PATH_TEXTURE");
            if (payload.NativePtr != null)
            {
                string path = Marshal.PtrToStringAnsi(payload.Data);
                if (!string.IsNullOrEmpty(path) && File.Exists(path))
                {
                    string newTextureName = Path.GetFileNameWithoutExtension(path);
                    var newTexture = _editor.ResourceManager.GetTexture(newTextureName);

                    _history.Execute(new ChangeMaterialTextureCommand(material, material.TextureName, material.Texture, newTextureName, newTexture));
                    dirty = true;
                }
            }
            ImGui.EndDragDropTarget();
        }

        ImGui.SameLine();
        ImGui.Text(material.TextureName);

        ImGui.TableSetColumnIndex(2);
        if (ImGui.ImageButton("ResetTexture", resetIcon, new Vector2(buttonSize, buttonSize)))
        {
            var newTexture = _editor.ResourceManager.GetTexture("White");
            _history.Execute(new ChangeMaterialTextureCommand(material, material.TextureName, material.Texture, "White", newTexture));
            dirty = true;
        }

        // Tiling
        ImGui.TableNextRow();
        ImGui.TableSetColumnIndex(0); ImGui.Text("Texture Tiling");
        ImGui.TableSetColumnIndex(1);
        var tilingBeforeEdit = material.TextureTiling;
        var tiling = tilingBeforeEdit;
        if (DefaultInspector.DrawVector2Control("##Tiling", ref tiling, out bool activated, out bool deactivated))
        {
            material.TextureTiling = tiling; dirty = true;
        }
        HandleUndo(material, nameof(Material.TextureTiling), tilingBeforeEdit, activated, deactivated);


        ImGui.TableSetColumnIndex(2);
        if (ImGui.ImageButton("ResetTiling", resetIcon, new Vector2(buttonSize, buttonSize)))
        {
            var valueBeforeReset = material.TextureTiling;
            if (valueBeforeReset != Vector2.One)
            {
                material.TextureTiling = Vector2.One;
                _history.Execute(new ChangePropertyCommand(material, typeof(Material).GetProperty(nameof(Material.TextureTiling)), valueBeforeReset, Vector2.One));
                dirty = true;
            }
        }


        // Emissive Color
        ImGui.TableNextRow();
        ImGui.TableSetColumnIndex(0); ImGui.Text("Emissive Color");
        ImGui.TableSetColumnIndex(1);
        var emissiveBeforeEdit = material.EmissiveColor;
        var emissive = emissiveBeforeEdit;
        if (DefaultInspector.DrawColor3Control("##Emissive", ref emissive, out bool emissiveActivated, out bool emissiveDeactivated))
        {
            material.EmissiveColor = emissive; dirty = true;
        }
        HandleUndo(material, nameof(Material.EmissiveColor), emissiveBeforeEdit, emissiveActivated, emissiveDeactivated);

        ImGui.TableSetColumnIndex(2);
        if (ImGui.ImageButton("ResetEmissive", resetIcon, new Vector2(buttonSize, buttonSize)))
        {
            var valueBeforeReset = material.EmissiveColor;
            if (valueBeforeReset != Vector3.Zero)
            {
                material.EmissiveColor = Vector3.Zero;
                _history.Execute(new ChangePropertyCommand(material, typeof(Material).GetProperty(nameof(Material.EmissiveColor)), valueBeforeReset, Vector3.Zero));
                dirty = true;
            }
        }

        ImGui.EndTable();
        return dirty;
    }

    private void HandleUndo(object target, string propertyName, object valueBeforeEdit, bool activated, bool deactivated)
    {
        var property = target.GetType().GetProperty(propertyName);
        if (property == null) return;

        if (activated)
        {
            _undoInitialValue = valueBeforeEdit;
        }

        if (deactivated)
        {
            object valueAfterEdit = property.GetValue(target);
            if (_undoInitialValue != null && !_undoInitialValue.Equals(valueAfterEdit))
            {
                property.SetValue(target, _undoInitialValue);
                _history.Execute(new ChangePropertyCommand(target, property, _undoInitialValue, valueAfterEdit));
            }
            _undoInitialValue = null;
        }
    }

    // Inner class for the specific command
    private class ChangeMaterialTextureCommand : ICommand
    {
        private readonly Material _target;
        private readonly string _oldTextureName;
        private readonly Cherris.Rendering.ITexture _oldTexture;
        private readonly string _newTextureName;
        private readonly Cherris.Rendering.ITexture _newTexture;

        public ChangeMaterialTextureCommand(Material target, string oldTextureName, Cherris.Rendering.ITexture oldTexture, string newTextureName, Cherris.Rendering.ITexture newTexture)
        {
            _target = target;
            _oldTextureName = oldTextureName;
            _oldTexture = oldTexture;
            _newTextureName = newTextureName;
            _newTexture = newTexture;
        }

        public void Execute()
        {
            _target.TextureName = _newTextureName;
            _target.Texture = _newTexture;
        }

        public void Undo()
        {
            _target.TextureName = _oldTextureName;
            _target.Texture = _oldTexture;
        }
    }
}