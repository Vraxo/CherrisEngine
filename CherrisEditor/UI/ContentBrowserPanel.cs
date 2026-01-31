using Cherris.Core;
using Cherris.Core.Logging;
using Cherris.Utils;
using ImGuiNET;
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

    public ContentBrowserPanel(Editor editor, EditorTextureManager textureManager)
    {
        _editor = editor;
        _textureManager = textureManager;

        string projectRoot = editor.ProjectManager.CurrentProject?.RootPath
            ?? throw new InvalidOperationException("ContentBrowserPanel requires an active project.");

        _currentDirectory = projectRoot;

        LoadIcon(IconFolder, "Icons/Folder.png");
        LoadIcon(IconFile, "Icons/File.png");
        LoadIcon(IconScript, "Icons/Components/Script.png");
        LoadIcon(IconPrefab, "Icons/Prefab.png");
    }

    private void LoadIcon(string key, string relativePath)
    {
        string? path = EditorResources.Find(relativePath);
        if (path is null)
        {
            Logger.Warning($"[ContentBrowser] Icon not found: '{relativePath}'");
            return;
        }
        _textureManager.LoadTexture(key, path);
    }

    public void Draw()
    {
        ImGui.Begin("Content Browser");

        DrawHeader();
        DrawGrid();
        DrawDropTarget();

        ImGui.End();
    }

    private void DrawHeader()
    {
        string projectRoot = _editor.ProjectManager.CurrentProject?.RootPath ?? _currentDirectory;

        if (ImGui.BeginTable("ContentBrowserHeader", 2, ImGuiTableFlags.SizingFixedFit))
        {
            ImGui.TableSetupColumn("BackButton", ImGuiTableColumnFlags.WidthFixed, 80.0f);
            ImGui.TableSetupColumn("PathText", ImGuiTableColumnFlags.WidthStretch);
            ImGui.TableNextRow();

            ImGui.TableSetColumnIndex(0);
            DrawBackButton(projectRoot);

            ImGui.TableSetColumnIndex(1);
            ImGui.AlignTextToFramePadding();
            ImGui.Text($"Path: {GetDisplayPath(projectRoot)}");

            ImGui.EndTable();
        }

        ImGui.Separator();
    }

    private string GetDisplayPath(string projectRoot)
    {
        return _currentDirectory == projectRoot
            ? _editor.ProjectManager.CurrentProject?.Name ?? "Project"
            : _currentDirectory.Replace(projectRoot, _editor.ProjectManager.CurrentProject?.Name ?? "Project");
    }

    private void DrawBackButton(string projectRoot)
    {
        if (_currentDirectory == projectRoot || !ImGui.Button("<- Back"))
        {
            return;
        }

        string? parent = Directory.GetParent(_currentDirectory)?.FullName;
        _currentDirectory = parent ?? projectRoot;
    }

    private void DrawGrid()
    {
        float panelWidth = ImGui.GetContentRegionAvail().X;
        int columnCount = Math.Max(1, (int)(panelWidth / CellSize));

        if (!ImGui.BeginTable("ContentGrid", columnCount))
        {
            return;
        }

        foreach (var directory in Directory.GetDirectories(_currentDirectory))
        {
            ImGui.TableNextColumn();
            RenderContentItem(directory, isDirectory: true);
        }

        foreach (var file in Directory.GetFiles(_currentDirectory).Where(IsNotMetadata))
        {
            ImGui.TableNextColumn();
            RenderContentItem(file, isDirectory: false);
        }

        ImGui.EndTable();
    }

    private static bool IsNotMetadata(string path)
    {
        string fileName = Path.GetFileName(path).ToLowerInvariant();
        return fileName is not "project.yaml" and not "project.yml";
    }

    private void RenderContentItem(string path, bool isDirectory)
    {
        string name = Path.GetFileName(path);
        IntPtr icon = isDirectory
            ? _textureManager.GetTexture(IconFolder)
            : _textureManager.GetTextureForPath(path);

        CenterAlignItem(ThumbnailSize);

        ImGui.ImageButton($"##item_{path}", icon, new(ThumbnailSize, ThumbnailSize));

        if (ImGui.BeginDragDropSource())
        {
            SendPayload(path, name, icon);
            ImGui.EndDragDropSource();
        }

        HandleItemInteraction(path, isDirectory);

        CenterAlignText(name);
        ImGui.Text(name);
    }

    private void SendPayload(string path, string name, IntPtr icon)
    {
        string? type = GetPayloadType(path);
        if (type is null)
        {
            return;
        }

        DragDropPayload.SendString(type, path);
        ImGui.Image(icon, new(50, 50));
        ImGui.SameLine();
        ImGui.Text(name);
    }

    private static string? GetPayloadType(string path)
    {
        string ext = Path.GetExtension(path).ToLowerInvariant();

        if (EditorTextureManager.ImageExtensions.Contains(ext))
        {
            return EditorConstants.DragDropPayloads.Texture;
        }

        if (EditorTextureManager.PrefabExtensions.Contains(ext))
        {
            return EditorConstants.DragDropPayloads.Prefab;
        }

        if (EditorTextureManager.MeshExtensions.Contains(ext))
        {
            return EditorConstants.DragDropPayloads.Mesh;
        }

        return null;
    }

    private void HandleItemInteraction(string path, bool isDirectory)
    {
        if (!ImGui.IsItemHovered() || !ImGui.IsMouseDoubleClicked(ImGuiMouseButton.Left))
        {
            return;
        }

        if (isDirectory)
        {
            _currentDirectory = path;
        }
        else if (path.EndsWith(".yaml", StringComparison.OrdinalIgnoreCase))
        {
            _editor.SceneOperations.LoadSceneFromFile(path);
        }
    }

    private void DrawDropTarget()
    {
        ImGui.InvisibleButton("ContentDropZone", ImGui.GetContentRegionAvail());

        if (!ImGui.BeginDragDropTarget())
        {
            return;
        }

        AcceptGameObjectDrop();
        ImGui.EndDragDropTarget();
    }

    private unsafe void AcceptGameObjectDrop()
    {
        var payload = ImGui.AcceptDragDropPayload(EditorConstants.DragDropPayloads.GameObjectId);
        if (payload.NativePtr is null || payload.DataSize != 16)
        {
            return;
        }

        byte[] data = new byte[16];
        Marshal.Copy(payload.Data, data, 0, 16);
        Guid objectId = new(data);

        GameObject? gameObject = _editor.SceneManager.GameObjects.FirstOrDefault(g => g.Id == objectId);
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