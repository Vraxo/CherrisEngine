using Cherris.Core.Logging;
using Cherris.Utils;
using ImGuiNET;
using System.Numerics;
using System.Runtime.InteropServices;

namespace CherrisEditor.UI;

public class ContentBrowserPanel : IDisposable
{
    private readonly Editor _editor;
    private readonly EditorTextureManager _textureManager;
    private string _currentAssetPath;
    private static IntPtr _payloadStringPtr = IntPtr.Zero;

    public ContentBrowserPanel(Editor editor, EditorTextureManager textureManager)
    {
        _editor = editor;
        _textureManager = textureManager;

        LoadIcon("Folder", "Icons/folder.png");
        LoadIcon("File", "Icons/file.png");
        LoadIcon("Script", "Icons/script.png");
        LoadIcon("Prefab", "Icons/prefab.png");

        _currentAssetPath = _editor.CurrentProject!.RootPath;
    }

    private void LoadIcon(string key, string relativePath)
    {
        string? path = EditorResources.Find(relativePath);
        if (path is not null)
        {
            _textureManager.LoadTexture(key, path);
        }
        else
        {
            Logger.Warning($"[ContentBrowser] Could not find icon '{relativePath}'");
        }
    }

    public void Draw()
    {
        if (_payloadStringPtr != IntPtr.Zero)
        {
            Marshal.FreeHGlobal(_payloadStringPtr);
            _payloadStringPtr = IntPtr.Zero;
        }

        _ = ImGui.Begin("Content Browser");
        DrawHeader();
        DrawGrid();

        if (ImGui.BeginDragDropTarget())
        {
            ImGuiPayloadPtr payload = ImGui.AcceptDragDropPayload("GAMEOBJECT_ID");

            unsafe
            {
                if (payload.NativePtr != null)
                {
                    byte[] data = new byte[payload.DataSize];
                    Marshal.Copy(payload.Data, data, 0, payload.DataSize);
                    var goId = new Guid(data);
                    var go = _editor.SceneManager.GameObjects.FirstOrDefault(g => g.Id == goId);

                    if (go is not null)
                    {
                        string prefabPath = Path.Combine(_currentAssetPath, $"{go.Name}.prefab");
                        _editor.CreatePrefabFromGameObject(go, prefabPath);
                    }
                }
            }

            ImGui.EndDragDropTarget();
        }

        ImGui.End();
    }

    private void DrawHeader()
    {
        if (_currentAssetPath != _editor.CurrentProject!.RootPath)
        {
            if (ImGui.Button("<- Back"))
            {
                _currentAssetPath = Directory.GetParent(_currentAssetPath)?.FullName ?? _editor.CurrentProject.RootPath;
            }

            ImGui.SameLine();
        }

        string displayPath = _currentAssetPath == _editor.CurrentProject.RootPath
            ? _editor.CurrentProject.Name
            : _currentAssetPath.Replace(_editor.CurrentProject.RootPath, _editor.CurrentProject.Name);

        ImGui.Text($"Path: {displayPath}");
        ImGui.Separator();
    }

    private void DrawGrid()
    {
        float thumbnailSize = 80.0f;
        float padding = 16.0f;
        float cellSize = thumbnailSize + padding;
        float panelWidth = ImGui.GetContentRegionAvail().X;
        int columnCount = Math.Max(1, (int)(panelWidth / cellSize));

        if (!ImGui.BeginTable("ContentGrid", columnCount))
        {
            return;
        }

        var directories = Directory.GetDirectories(_currentAssetPath);
        var files = Directory.GetFiles(_currentAssetPath)
            .Where(f => !IsProjectMetadata(f))
            .ToArray();

        foreach (var path in directories.Concat(files))
        {
            _ = ImGui.TableNextColumn();
            DrawItem(path, thumbnailSize);
        }

        ImGui.EndTable();
    }

    private static bool IsProjectMetadata(string path)
    {
        string fileName = Path.GetFileName(path).ToLowerInvariant();
        return fileName is "project.yaml" or "project.yml";
    }

    private void DrawItem(string path, float thumbnailSize)
    {
        ImGui.PushID(path);
        ImGui.PushStyleColor(ImGuiCol.Button, Vector4.Zero);

        IntPtr textureHandle = _textureManager.GetTextureForPath(path);
        string itemName = Path.GetFileName(path);

        CenterAlignItem(thumbnailSize);
        if (ImGui.ImageButton(itemName, textureHandle, new Vector2(thumbnailSize, thumbnailSize)))
        {
        }

        string extension = Path.GetExtension(path).ToLowerInvariant();

        string? payloadType = null;
        if (EditorTextureManager.ImageExtensions.Contains(extension))
        {
            payloadType = "ASSET_PATH_TEXTURE";
        }
        else if (EditorTextureManager.PrefabExtensions.Contains(extension))
        {
            payloadType = "ASSET_PATH_PREFAB";
        }

        if (payloadType is not null && ImGui.BeginDragDropSource())
        {
            _payloadStringPtr = Marshal.StringToHGlobalAnsi(path);
            _ = ImGui.SetDragDropPayload(payloadType, _payloadStringPtr, (uint)(path.Length + 1));

            ImGui.Image(textureHandle, new Vector2(50, 50));
            ImGui.SameLine();
            ImGui.Text(itemName);
            ImGui.EndDragDropSource();
        }


        if (ImGui.IsItemHovered() && ImGui.IsMouseDoubleClicked(ImGuiMouseButton.Left))
        {
            if (Directory.Exists(path))
            {
                _currentAssetPath = path;
            }
            else if (Path.GetExtension(path).Equals(".yaml", StringComparison.OrdinalIgnoreCase))
            {
                _editor.LoadSceneFromFile(path);
            }
        }

        CenterAlignText(itemName);
        ImGui.Text(itemName);

        ImGui.PopStyleColor();
        ImGui.PopID();
    }

    private void CenterAlignItem(float itemWidth)
    {
        float columnWidth = ImGui.GetColumnWidth();
        float offsetX = (columnWidth - itemWidth) * 0.5f;

        ImGui.SetCursorPosX(ImGui.GetCursorPosX() + offsetX);
    }

    private void CenterAlignText(string text)
    {
        float columnWidth = ImGui.GetColumnWidth();
        float textWidth = ImGui.CalcTextSize(text).X;
        float textOffsetX = (columnWidth - textWidth) * 0.5f;

        ImGui.SetCursorPosX(ImGui.GetCursorPosX() + textOffsetX);
    }

    public void Dispose()
    {
        if (_payloadStringPtr == IntPtr.Zero)
        {
            return;
        }

        Marshal.FreeHGlobal(_payloadStringPtr);
        _payloadStringPtr = IntPtr.Zero;
    }
}