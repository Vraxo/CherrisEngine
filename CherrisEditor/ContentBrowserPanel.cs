using Cherris;
using ImGuiNET;
using System;
using System.IO;
using System.Linq;
using System.Numerics;
using System.Runtime.InteropServices;

namespace CherrisEditor;

public class ContentBrowserPanel : IDisposable
{
    private readonly Editor _editor;
    private readonly EditorTextureManager _textureManager;
    private readonly string _assetRootPath;
    private string _currentAssetPath;
    private static IntPtr _payloadPtr = IntPtr.Zero;

    public ContentBrowserPanel(Editor editor, EditorTextureManager textureManager)
    {
        _editor = editor;
        _textureManager = textureManager;
        _textureManager.LoadTexture("Folder", "Assets/Icons/folder.png");
        _textureManager.LoadTexture("File", "Assets/Icons/file.png");
        _textureManager.LoadTexture("Script", "Assets/Icons/script.png");

        _assetRootPath = Path.GetFullPath("Assets");
        _currentAssetPath = _assetRootPath;
    }

    public void Draw()
    {
        // Free any unmanaged memory from the previous frame's drag-drop operation.
        if (_payloadPtr != IntPtr.Zero)
        {
            Marshal.FreeHGlobal(_payloadPtr);
            _payloadPtr = IntPtr.Zero;
        }

        ImGui.Begin("Content Browser");
        DrawHeader();
        DrawGrid();
        ImGui.End();
    }

    private void DrawHeader()
    {
        if (_currentAssetPath != _assetRootPath)
        {
            if (ImGui.Button("<- Back"))
            {
                _currentAssetPath = Directory.GetParent(_currentAssetPath)?.FullName ?? _assetRootPath;
            }
            ImGui.SameLine();
        }
        ImGui.Text($"Path: {_currentAssetPath.Replace(_assetRootPath, "Assets")}");
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
        var files = Directory.GetFiles(_currentAssetPath);

        foreach (var path in directories.Concat(files))
        {
            ImGui.TableNextColumn();
            DrawItem(path, thumbnailSize);
        }

        ImGui.EndTable();
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
            // Handle single-click
        }

        // --- DRAG SOURCE LOGIC ---
        string extension = Path.GetExtension(path).ToLowerInvariant();
        if (EditorTextureManager.ImageExtensions.Contains(extension))
        {
            if (ImGui.BeginDragDropSource())
            {
                // Set payload to be the file path
                _payloadPtr = Marshal.StringToHGlobalAnsi(path);
                ImGui.SetDragDropPayload("ASSET_PATH_TEXTURE", _payloadPtr, (uint)(path.Length + 1));

                // Show a preview while dragging
                ImGui.Image(textureHandle, new Vector2(50, 50));
                ImGui.SameLine();
                ImGui.Text(itemName);

                ImGui.EndDragDropSource();
            }
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
        // Ensure we free the handle on shutdown if it's still allocated
        if (_payloadPtr != IntPtr.Zero)
        {
            Marshal.FreeHGlobal(_payloadPtr);
            _payloadPtr = IntPtr.Zero;
        }
    }
}