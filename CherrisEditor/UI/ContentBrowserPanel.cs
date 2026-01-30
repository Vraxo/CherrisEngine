using Cherris.Core.Logging;
using Cherris.Utils;
using ImGuiNET;
using System.Numerics;
using System.Runtime.InteropServices;

namespace CherrisEditor.UI;

public class ContentBrowserPanel : IDisposable
{
    private const string IconFolder = "Folder";
    private const string IconFile = "File";
    private const string IconScript = "Script";
    private const string IconPrefab = "Prefab";

    private const float ThumbnailSize = 80.0f;
    private const float CellPadding = 16.0f;
    private const float CellSize = ThumbnailSize + CellPadding;

    private readonly Editor _editor;
    private readonly EditorTextureManager _textureManager;
    private string _currentDirectory;

    private static readonly string[] MeshExtensions = { ".obj", ".gltf", ".glb", ".fbx", ".dae" };

    public ContentBrowserPanel(Editor editor, EditorTextureManager textureManager)
    {
        _editor = editor;
        _textureManager = textureManager;

        var projectRoot = editor.ProjectManager.CurrentProject?.RootPath
            ?? throw new InvalidOperationException("ContentBrowserPanel requires an active project.");

        _currentDirectory = projectRoot;
        LoadIcons();
    }

    private void LoadIcons()
    {
        LoadIcon(IconFolder, "Icons/Folder.png");
        LoadIcon(IconFile, "Icons/File.png");
        LoadIcon(IconScript, "Icons/Components/Script.png");
        LoadIcon(IconPrefab, "Icons/Prefab.png");
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
            Logger.Warning($"[ContentBrowser] Icon not found: '{relativePath}'");
        }
    }

    public void Draw()
    {
        _ = ImGui.Begin("Content Browser");

        DrawHeader();
        DrawGrid();
        DrawDropTarget();

        ImGui.End();
    }

    private void DrawHeader()
    {
        string projectRoot = _editor.ProjectManager.CurrentProject?.RootPath ?? _currentDirectory;

        if (_currentDirectory != projectRoot)
        {
            if (ImGui.Button("<- Back"))
            {
                _currentDirectory = Directory.GetParent(_currentDirectory)?.FullName ?? projectRoot;
            }
            ImGui.SameLine();
        }

        string displayPath = _currentDirectory == projectRoot
            ? _editor.ProjectManager.CurrentProject?.Name ?? "Project"
            : _currentDirectory.Replace(projectRoot, _editor.ProjectManager.CurrentProject?.Name ?? "Project");

        ImGui.Text($"Path: {displayPath}");
        ImGui.Separator();
    }

    private void DrawGrid()
    {
        float panelWidth = ImGui.GetContentRegionAvail().X;
        int columnCount = Math.Max(1, (int)(panelWidth / CellSize));

        if (!ImGui.BeginTable("ContentGrid", columnCount))
        {
            return;
        }

        var directories = Directory.GetDirectories(_currentDirectory);
        var files = Directory.GetFiles(_currentDirectory)
            .Where(f => !IsProjectMetadata(f))
            .ToArray();

        foreach (var directory in directories)
        {
            _ = ImGui.TableNextColumn();
            RenderDirectory(directory);
        }

        foreach (var file in files)
        {
            _ = ImGui.TableNextColumn();
            RenderFile(file);
        }

        ImGui.EndTable();
    }

    private static bool IsProjectMetadata(string path)
    {
        string fileName = Path.GetFileName(path).ToLowerInvariant();
        return fileName is "project.yaml" or "project.yml";
    }

    private void RenderDirectory(string path)
    {
        string name = Path.GetFileName(path);
        IntPtr icon = _textureManager.GetTexture(IconFolder);

        CenterAlignItem(ThumbnailSize);

        if (ImGui.ImageButton($"##dir_{path}", icon, new Vector2(ThumbnailSize, ThumbnailSize)))
        {
        }

        if (ImGui.IsItemHovered() && ImGui.IsMouseDoubleClicked(ImGuiMouseButton.Left))
        {
            _currentDirectory = path;
        }

        CenterAlignText(name);
        ImGui.Text(name);
    }

    private void RenderFile(string path)
    {
        string name = Path.GetFileName(path);
        IntPtr icon = _textureManager.GetTextureForPath(path);

        CenterAlignItem(ThumbnailSize);

        if (ImGui.ImageButton($"##file_{path}", icon, new Vector2(ThumbnailSize, ThumbnailSize)))
        {
        }

        HandleFileDragDrop(path, name, icon);
        HandleFileDoubleClick(path, name);

        CenterAlignText(name);
        ImGui.Text(name);
    }

    private static void HandleFileDragDrop(string path, string name, IntPtr icon)
    {
        if (!ImGui.BeginDragDropSource())
        {
            return;
        }

        string? payloadType = DeterminePayloadType(path);

        if (payloadType is not null)
        {
            SendStringPayload(payloadType, path);

            ImGui.Image(icon, new Vector2(50, 50));
            ImGui.SameLine();
            ImGui.Text(name);
        }

        ImGui.EndDragDropSource();
    }

    private static string? DeterminePayloadType(string path)
    {
        string extension = Path.GetExtension(path).ToLowerInvariant();

        if (EditorTextureManager.ImageExtensions.Contains(extension))
        {
            return "ASSET_PATH_TEXTURE";
        }

        return EditorTextureManager.PrefabExtensions.Contains(extension)
            ? "ASSET_PATH_PREFAB"
            : MeshExtensions.Contains(extension) ? "ASSET_PATH_MESH" : null;
    }

    private static void SendStringPayload(string payloadType, string data)
    {
        IntPtr ptr = Marshal.StringToHGlobalAnsi(data);
        try
        {
            uint size = (uint)(data.Length + 1);
            _ = ImGui.SetDragDropPayload(payloadType, ptr, size);
        }
        finally
        {
            Marshal.FreeHGlobal(ptr);
        }
    }

    private void HandleFileDoubleClick(string path, string name)
    {
        if (!ImGui.IsItemHovered() || !ImGui.IsMouseDoubleClicked(ImGuiMouseButton.Left))
        {
            return;
        }

        if (name.EndsWith(".yaml", StringComparison.OrdinalIgnoreCase))
        {
            _editor.SceneOperations.LoadSceneFromFile(path);
        }
    }

    private void DrawDropTarget()
    {
        _ = ImGui.InvisibleButton("ContentDropZone", ImGui.GetContentRegionAvail());

        if (!ImGui.BeginDragDropTarget())
        {
            return;
        }

        HandleGameObjectDrop();

        ImGui.EndDragDropTarget();
    }

    private unsafe void HandleGameObjectDrop()
    {
        ImGuiPayloadPtr payload = ImGui.AcceptDragDropPayload("GAMEOBJECT_ID");

        if (payload.NativePtr is null)
        {
            return;
        }

        byte[] data = new byte[payload.DataSize];
        Marshal.Copy(payload.Data, data, 0, payload.DataSize);
        var objectId = new Guid(data);

        var gameObject = _editor.SceneManager.GameObjects.FirstOrDefault(g => g.Id == objectId);

        if (gameObject is null)
        {
            return;
        }

        string prefabPath = Path.Combine(_currentDirectory, $"{gameObject.Name}.prefab");
        _editor.SceneOperations.CreatePrefabFromGameObject(gameObject, prefabPath);
    }

    private static void CenterAlignItem(float itemWidth)
    {
        float columnWidth = ImGui.GetColumnWidth();
        float offsetX = (columnWidth - itemWidth) * 0.5f;
        ImGui.SetCursorPosX(ImGui.GetCursorPosX() + offsetX);
    }

    private static void CenterAlignText(string text)
    {
        float columnWidth = ImGui.GetColumnWidth();
        float textWidth = ImGui.CalcTextSize(text).X;
        float offsetX = (columnWidth - textWidth) * 0.5f;
        ImGui.SetCursorPosX(ImGui.GetCursorPosX() + offsetX);
    }

    public void Dispose()
    {
        GC.SuppressFinalize(this);
    }
}