using Cherris;
using ImGuiNET;
using System;
using System.IO;
using System.Numerics;
using System.Runtime.InteropServices;

namespace CherrisEditor.Inspectors;

[CustomInspector(typeof(MeshRenderer))]
public class MeshRendererInspector : IComponentInspector
{
    private readonly Editor _editor;
    private readonly EditorTextureManager _textureManager;

    // We need access to the editor's systems, so we'll pass them in.
    public MeshRendererInspector(Editor editor, EditorTextureManager textureManager)
    {
        _editor = editor;
        _textureManager = textureManager;
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
        float buttonSize = ImGui.GetFrameHeight() - 4; // A bit of padding

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
                    string textureName = Path.GetFileNameWithoutExtension(path);
                    var newTexture = _editor.ResourceManager.GetTexture(textureName);
                    mr.TextureName = textureName;
                    mr.Texture = newTexture;
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
            mr.TextureName = "White";
            mr.Texture = _editor.ResourceManager.GetTexture("White");
            dirty = true;
        }

        // Tiling
        ImGui.TableNextRow();
        ImGui.TableSetColumnIndex(0);
        ImGui.Text("Texture Tiling");
        ImGui.TableSetColumnIndex(1);
        var tiling = mr.TextureTiling;
        if (DefaultInspector.DrawVector2Control("##Tiling", ref tiling))
        {
            mr.TextureTiling = tiling;
            dirty = true;
        }
        ImGui.TableSetColumnIndex(2);
        if (ImGui.ImageButton("ResetTiling", resetIcon, new Vector2(buttonSize, buttonSize)))
        {
            mr.TextureTiling = Vector2.One;
            dirty = true;
        }

        // Emissive Color
        ImGui.TableNextRow();
        ImGui.TableSetColumnIndex(0);
        ImGui.Text("Emissive Color");
        ImGui.TableSetColumnIndex(1);
        var emissive = mr.EmissiveColor;
        if (DefaultInspector.DrawColor3Control("##Emissive", ref emissive))
        {
            mr.EmissiveColor = emissive;
            dirty = true;
        }
        ImGui.TableSetColumnIndex(2);
        if (ImGui.ImageButton("ResetEmissive", resetIcon, new Vector2(buttonSize, buttonSize)))
        {
            mr.EmissiveColor = Vector3.Zero;
            dirty = true;
        }

        ImGui.EndTable();
        return dirty;
    }
}