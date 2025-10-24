using ImGuiNET;
using System.Numerics;

namespace CherrisEditor;

public class ContentBrowserPanel : IDisposable
{
    private readonly IconManager _iconManager;
    private readonly string _assetRootPath;
    private string _currentAssetPath;

    public ContentBrowserPanel()
    {
        _iconManager = new IconManager();
        _iconManager.LoadIcon("Folder", "Assets/Icons/folder.png");
        _iconManager.LoadIcon("File", "Assets/Icons/file.png");

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

        bool isDirectory = Directory.Exists(path);
        IntPtr iconHandle = isDirectory ? _iconManager.GetIcon("Folder") : _iconManager.GetIcon("File");
        string itemName = Path.GetFileName(path);

        CenterAlignItem(thumbnailSize);
        if (ImGui.ImageButton(itemName, iconHandle, new Vector2(thumbnailSize, thumbnailSize)))
        {
            // Handle single-click
        }

        if (isDirectory && ImGui.IsItemHovered() && ImGui.IsMouseDoubleClicked(ImGuiMouseButton.Left))
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
        _iconManager.Dispose();
    }
}