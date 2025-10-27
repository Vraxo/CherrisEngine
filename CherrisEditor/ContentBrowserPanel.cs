using ImGuiNET;
using System.Numerics;
using System.Runtime.InteropServices;

namespace CherrisEditor;

public class ContentBrowserPanel : IDisposable
{
    private readonly Editor _editor;
    private readonly EditorTextureManager _textureManager;
    private readonly string _assetRootPath;
    private string _currentAssetPath;
    private static IntPtr _payloadStringPtr = IntPtr.Zero; // For string payloads

    public ContentBrowserPanel(Editor editor, EditorTextureManager textureManager)
    {
        _editor = editor;
        _textureManager = textureManager;
        _textureManager.LoadTexture("Folder", "Assets/Icons/folder.png");
        _textureManager.LoadTexture("File", "Assets/Icons/file.png");
        _textureManager.LoadTexture("Script", "Assets/Icons/script.png");
        _textureManager.LoadTexture("Prefab", "Assets/Icons/prefab.png"); // Added for prefabs

        _assetRootPath = Path.GetFullPath("Assets");
        _currentAssetPath = _assetRootPath;
    }

    public void Draw()
    {
        // Free the unmanaged memory from the *previous* frame's drag-drop operation.
        if (_payloadStringPtr != IntPtr.Zero)
        {
            Marshal.FreeHGlobal(_payloadStringPtr);
            _payloadStringPtr = IntPtr.Zero;
        }

        ImGui.Begin("Content Browser");
        DrawHeader();
        DrawGrid();

        // Make the entire panel a drop target for creating prefabs
        if (ImGui.BeginDragDropTarget())
        {
            ImGuiPayloadPtr payload = ImGui.AcceptDragDropPayload("GAMEOBJECT_ID");
            if (payload.Data != IntPtr.Zero)
            {
                byte[] data = new byte[payload.DataSize];
                Marshal.Copy(payload.Data, data, 0, payload.DataSize);
                var goId = new Guid(data);
                var go = _editor.SceneManager.GameObjects.FirstOrDefault(g => g.Id == goId);

                if (go != null)
                {
                    string prefabPath = Path.Combine(_currentAssetPath, $"{go.Name}.prefab");
                    _editor.CreatePrefabFromGameObject(go, prefabPath);
                }
            }
            ImGui.EndDragDropTarget();
        }

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

        string extension = Path.GetExtension(path).ToLowerInvariant();

        // --- Drag Source for Textures and Prefabs (string path payload) ---
        string payloadType = null;
        if (EditorTextureManager.ImageExtensions.Contains(extension)) payloadType = "ASSET_PATH_TEXTURE";
        else if (EditorTextureManager.PrefabExtensions.Contains(extension)) payloadType = "ASSET_PATH_PREFAB";

        if (payloadType != null && ImGui.BeginDragDropSource())
        {
            // Allocate memory and hold onto the pointer until the next frame.
            _payloadStringPtr = Marshal.StringToHGlobalAnsi(path);
            ImGui.SetDragDropPayload(payloadType, _payloadStringPtr, (uint)(path.Length + 1));

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
        // Ensure we free the handle on shutdown if it's still allocated
        if (_payloadStringPtr != IntPtr.Zero)
        {
            Marshal.FreeHGlobal(_payloadStringPtr);
            _payloadStringPtr = IntPtr.Zero;
        }
    }
}