using Cherris;
using ImGuiNET;
using System;
using System.IO;
using System.Linq;
using System.Numerics;

namespace CherrisEditor;

public class ContentBrowserPanel : IDisposable
{
    private readonly EditorTextureManager _textureManager;
    private readonly string _assetRootPath;
    private string _currentAssetPath;

    public ContentBrowserPanel()
    {
        _textureManager = new EditorTextureManager();
        _textureManager.LoadTexture("Folder", "Assets/Icons/folder.png");
        _textureManager.LoadTexture("File", "Assets/Icons/file.png");
        _textureManager.LoadTexture("Script", "Assets/Icons/script.png");

        _assetRootPath = Path.GetFullPath("Assets");
        _currentAssetPath = _assetRootPath;
    }

    public void Draw()
    {
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

        if (Directory.Exists(path) && ImGui.IsItemHovered() && ImGui.IsMouseDoubleClicked(ImGuiMouseButton.Left))
        {
            _currentAssetPath = path;
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
        _textureManager.Dispose();
    }
}