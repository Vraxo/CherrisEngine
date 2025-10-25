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

    public unsafe void Draw(Component component)
    {
        var mr = (MeshRenderer)component;

        if (!ImGui.BeginTable("MRTable", 3, ImGuiTableFlags.Resizable)) return;
        ImGui.TableSetupColumn("Property", ImGuiTableColumnFlags.WidthFixed, 120.0f);
        ImGui.TableSetupColumn("Value", ImGuiTableColumnFlags.WidthStretch);
        ImGui.TableSetupColumn("##Reset", ImGuiTableColumnFlags.WidthFixed, 25.0f);

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
                }
            }
            ImGui.EndDragDropTarget();
        }

        ImGui.SameLine();
        ImGui.Text(mr.TextureName);

        ImGui.TableSetColumnIndex(2);
        if (ImGui.Button("R##Texture"))
        {
            mr.TextureName = "White";
            mr.Texture = _editor.ResourceManager.GetTexture("White");
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
        }
        ImGui.TableSetColumnIndex(2);
        if (ImGui.Button("R##Tiling")) mr.TextureTiling = Vector2.One;

        // Emissive Color
        ImGui.TableNextRow();
        ImGui.TableSetColumnIndex(0);
        ImGui.Text("Emissive Color");
        ImGui.TableSetColumnIndex(1);
        var emissive = mr.EmissiveColor;
        if (DefaultInspector.DrawColor3Control("##Emissive", ref emissive))
        {
            mr.EmissiveColor = emissive;
        }
        ImGui.TableSetColumnIndex(2);
        if (ImGui.Button("R##Emissive")) mr.EmissiveColor = Vector3.Zero;

        ImGui.EndTable();
    }

    // This method is required by the interface but isn't used for custom inspectors.
    void IComponentInspector.Draw(Component component) => Draw(component);
}