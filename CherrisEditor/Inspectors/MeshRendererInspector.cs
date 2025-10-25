using Cherris;
using ImGuiNET;
using System;
using System.IO;
using System.Numerics;
using System.Reflection;
using System.Runtime.InteropServices;
using CherrisEditor.Undo;
using CherrisEditor.Undo.Commands;

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
        bool dirty = false;

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
        if (mr.Texture?.GetBackendHandle() is int handle && handle != 0)
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

                    _history.Execute(new ChangeTextureCommand(mr, mr.TextureName, mr.Texture, newTextureName, newTexture, _editor.ResourceManager));
                    dirty = true;
                }
            }
            ImGui.EndDragDropTarget();
        }

        ImGui.SameLine();
        ImGui.Text(mr.TextureName);

        ImGui.TableSetColumnIndex(2);
        if (ImGui.ImageButton("ResetTexture", resetIcon, new Vector2(buttonSize, buttonSize)))
        {
            var newTexture = _editor.ResourceManager.GetTexture("White");
            _history.Execute(new ChangeTextureCommand(mr, mr.TextureName, mr.Texture, "White", newTexture, _editor.ResourceManager));
            dirty = true;
        }

        // Tiling
        ImGui.TableNextRow();
        ImGui.TableSetColumnIndex(0); ImGui.Text("Texture Tiling");
        ImGui.TableSetColumnIndex(1);
        var tiling = mr.TextureTiling;
        if (DefaultInspector.DrawVector2Control("##Tiling", ref tiling)) { mr.TextureTiling = tiling; dirty = true; }
        HandleUndo(mr, nameof(mr.TextureTiling), mr.TextureTiling);

        ImGui.TableSetColumnIndex(2);
        if (ImGui.ImageButton("ResetTiling", resetIcon, new Vector2(buttonSize, buttonSize))) { mr.TextureTiling = Vector2.One; dirty = true; }
        HandleUndo(mr, nameof(mr.TextureTiling), mr.TextureTiling, true);


        // Emissive Color
        ImGui.TableNextRow();
        ImGui.TableSetColumnIndex(0); ImGui.Text("Emissive Color");
        ImGui.TableSetColumnIndex(1);
        var emissive = mr.EmissiveColor;
        if (DefaultInspector.DrawColor3Control("##Emissive", ref emissive)) { mr.EmissiveColor = emissive; dirty = true; }
        HandleUndo(mr, nameof(mr.EmissiveColor), mr.EmissiveColor);

        ImGui.TableSetColumnIndex(2);
        if (ImGui.ImageButton("ResetEmissive", resetIcon, new Vector2(buttonSize, buttonSize))) { mr.EmissiveColor = Vector3.Zero; dirty = true; }
        HandleUndo(mr, nameof(mr.EmissiveColor), mr.EmissiveColor, true);

        ImGui.EndTable();
        return dirty;
    }

    private void HandleUndo(object target, string propertyName, object oldValue, bool force = false)
    {
        var property = target.GetType().GetProperty(propertyName);
        if (property == null) return;

        if (ImGui.IsItemActivated() || force)
        {
            _undoInitialValue = oldValue;
        }

        if (ImGui.IsItemDeactivatedAfterEdit() || force)
        {
            object newValue = property.GetValue(target);
            if (_undoInitialValue != null && !_undoInitialValue.Equals(newValue))
            {
                property.SetValue(target, _undoInitialValue);
                _history.Execute(new ChangePropertyCommand(target, property, _undoInitialValue, newValue));
            }
            _undoInitialValue = null;
        }
    }

    // Inner class for the specific command
    private class ChangeTextureCommand : ICommand
    {
        private readonly MeshRenderer _target;
        private readonly string _oldTextureName;
        private readonly Cherris.Rendering.ITexture _oldTexture;
        private readonly string _newTextureName;
        private readonly Cherris.Rendering.ITexture _newTexture;
        private readonly Cherris.Rendering.IResourceManager _resourceManager;

        public ChangeTextureCommand(MeshRenderer target, string oldTextureName, Cherris.Rendering.ITexture oldTexture, string newTextureName, Cherris.Rendering.ITexture newTexture, Cherris.Rendering.IResourceManager resourceManager)
        {
            _target = target;
            _oldTextureName = oldTextureName;
            _oldTexture = oldTexture;
            _newTextureName = newTextureName;
            _newTexture = newTexture;
            _resourceManager = resourceManager;
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