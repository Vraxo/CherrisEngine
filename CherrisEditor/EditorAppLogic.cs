using Cherris;
using ImGuiNET;
using System.Numerics;
using System.IO;
using System;
using System.Linq;

namespace CherrisEditor;

public class EditorAppLogic : IDisposable
{
    private readonly Editor _editor;
    private readonly InspectorPanel _inspectorPanel;
    private readonly IconManager _iconManager;

    private readonly string _assetRootPath;
    private string _currentAssetPath;

    public EditorAppLogic(Editor editor)
    {
        _editor = editor;
        _inspectorPanel = new(_editor);
        EditorTheme.ApplyUnrealEngineStyle();

        _iconManager = new IconManager();
        _iconManager.LoadIcon("Folder", "Assets/Icons/folder.png");
        _iconManager.LoadIcon("File", "Assets/Icons/file.png");

        _assetRootPath = Path.GetFullPath("Assets");
        _currentAssetPath = _assetRootPath;
    }

    public void Dispose()
    {
        _iconManager.Dispose();
    }

    public Action<float> DrawUI()
    {
        return (deltaTime) =>
        {
            SetupDockspace();

            DrawOutlinerPanel();
            DrawConsolePanel();
            DrawContentBrowserPanel();
            _inspectorPanel.DrawInspectorPanel();
        };
    }

    private void SetupDockspace()
    {
        ImGuiViewportPtr viewport = ImGui.GetMainViewport();
        ImGui.SetNextWindowPos(viewport.Pos);
        ImGui.SetNextWindowSize(viewport.Size);
        ImGui.SetNextWindowViewport(viewport.ID);

        ImGui.PushStyleVar(ImGuiStyleVar.WindowRounding, 0.0f);
        ImGui.PushStyleVar(ImGuiStyleVar.WindowBorderSize, 0.0f);
        ImGui.PushStyleVar(ImGuiStyleVar.WindowPadding, Vector2.Zero);

        ImGuiWindowFlags windowFlags =
              ImGuiWindowFlags.NoTitleBar
            | ImGuiWindowFlags.NoCollapse
            | ImGuiWindowFlags.NoResize
            | ImGuiWindowFlags.NoMove
            | ImGuiWindowFlags.NoBringToFrontOnFocus
            | ImGuiWindowFlags.NoNavFocus
            | ImGuiWindowFlags.MenuBar
            | ImGuiWindowFlags.NoBackground;

        ImGui.Begin("MainDockspace", windowFlags);
        ImGui.PopStyleVar(3);

        uint dockspaceId = ImGui.GetID("MyDockSpace");
        ImGui.DockSpace(dockspaceId, Vector2.Zero, ImGuiDockNodeFlags.PassthruCentralNode);
        DrawMainMenuBar();
        ImGui.End();
    }

    private static void DrawMainMenuBar()
    {
        if (!ImGui.BeginMenuBar()) return;

        if (ImGui.BeginMenu("File"))
        {
            if (ImGui.MenuItem("New Scene")) { }
            if (ImGui.MenuItem("Open Scene")) { }
            ImGui.Separator();
            if (ImGui.MenuItem("Save")) { }
            if (ImGui.MenuItem("Save As...")) { }
            ImGui.Separator();
            if (ImGui.MenuItem("Exit")) { Console.WriteLine("Exit clicked!"); }
            ImGui.EndMenu();
        }

        ImGui.EndMenuBar();
    }

    private void DrawOutlinerPanel()
    {
        ImGui.Begin("Outliner");

        foreach (GameObject go in _editor.SceneManager.GameObjects)
        {
            bool isSelected = _editor.GetSelectedGameObject() == go;

            if (ImGui.Selectable(go.Name, isSelected))
            {
                _editor.SetSelectedGameObject(go);
            }
        }

        ImGui.End();
    }

    private static void DrawConsolePanel()
    {
        ImGui.Begin("Console");
        ImGui.Text("Log messages will appear here...");
        ImGui.End();
    }

    private void DrawContentBrowserPanel()
    {
        ImGui.Begin("Content Browser");

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

        float thumbnailSize = 80.0f;
        float padding = 16.0f;
        float cellSize = thumbnailSize + padding;
        float panelWidth = ImGui.GetContentRegionAvail().X;
        int columnCount = (int)(panelWidth / cellSize);
        if (columnCount < 1) columnCount = 1;

        if (ImGui.BeginTable("ContentGrid", columnCount))
        {
            var directories = Directory.GetDirectories(_currentAssetPath);
            var files = Directory.GetFiles(_currentAssetPath);

            foreach (var path in directories.Concat(files))
            {
                ImGui.TableNextColumn();
                ImGui.PushID(path);

                bool isDirectory = Directory.Exists(path);
                IntPtr iconHandle = isDirectory ? _iconManager.GetIcon("Folder") : _iconManager.GetIcon("File");

                string itemName = Path.GetFileName(path);

                ImGui.PushStyleColor(ImGuiCol.Button, Vector4.Zero);

                // --- START OF FIX ---
                // The correct overload requires a string ID as the first argument.
                // We use the unique itemName for this purpose.
                ImGui.ImageButton(itemName, iconHandle, new Vector2(thumbnailSize, thumbnailSize), new Vector2(0, 0), new Vector2(1, 1));
                // --- END OF FIX ---

                ImGui.PopStyleColor();

                if (isDirectory && ImGui.IsItemHovered() && ImGui.IsMouseDoubleClicked(ImGuiMouseButton.Left))
                {
                    _currentAssetPath = path;
                }

                ImGui.PushTextWrapPos(ImGui.GetCursorPosX() + thumbnailSize);
                ImGui.TextWrapped(itemName);
                ImGui.PopTextWrapPos();

                ImGui.PopID();
            }

            ImGui.EndTable();
        }

        ImGui.End();
    }

    public void UpdateEditorLogic(float deltaTime)
    {
        HandleObjectSelection();
        HandleMovement(deltaTime);
    }

    private void HandleObjectSelection()
    {
        if (!Input.WasMouseButtonPressed(MouseButton.Left) || ImGui.GetIO().WantCaptureMouse)
        {
            return;
        }

        Ray ray = _editor.CreateRayFromMouse();
        GameObject? closestObject = null;
        float closestDistance = float.MaxValue;

        foreach (GameObject gameObject in _editor.SceneManager.GameObjects)
        {
            if (gameObject.GetComponent<Skybox>() is not null || gameObject.GetComponent<Camera>() is not null)
            {
                continue;
            }

            BoundingBox aabb = gameObject.GetWorldSpaceAABB();

            if (!ray.Intersects(aabb, out float distance))
            {
                continue;
            }

            if (distance >= closestDistance)
            {
                continue;
            }

            closestDistance = distance;
            closestObject = gameObject;
        }

        _editor.SetSelectedGameObject(closestObject);

        if (closestObject is not null)
        {
            Console.WriteLine($"Selected '{closestObject.Name}'");
        }
    }

    private void HandleMovement(float deltaTime)
    {
        GameObject? selectedGameObject = _editor.GetSelectedGameObject();

        if (selectedGameObject is null)
        {
            return;
        }

        const float moveSpeed = 2.0f;
        Vector3 moveDirection = Vector3.Zero;
        bool shiftHeld = Input.IsKeyDown(Key.ShiftLeft) || Input.IsKeyDown(Key.ShiftRight);

        if (Input.IsKeyDown(Key.Left))
        {
            moveDirection.X -= 1;
        }

        if (Input.IsKeyDown(Key.Right))
        {
            moveDirection.X += 1;
        }

        if (shiftHeld)
        {
            if (Input.IsKeyDown(Key.Up))
            {
                moveDirection.Y += 1;
            }

            if (Input.IsKeyDown(Key.Down))
            {
                moveDirection.Y -= 1;
            }
        }
        else
        {
            if (Input.IsKeyDown(Key.Up))
            {
                moveDirection.Z -= 1;
            }

            if (Input.IsKeyDown(Key.Down))
            {
                moveDirection.Z += 1;
            }
        }

        if (moveDirection != Vector3.Zero)
        {
            selectedGameObject.Transform.Position += Vector3.Normalize(moveDirection) * moveSpeed * deltaTime;
        }
    }
}