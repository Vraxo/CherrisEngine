using Cherris;
using ImGuiNET;
using System.Numerics;
using System.IO; // Required for file system operations
using System; // Required for Path

namespace CherrisEditor;

public class EditorAppLogic
{
    private readonly Editor _editor;
    private readonly InspectorPanel _inspectorPanel;

    // --- State for the Content Browser ---
    private readonly string _assetRootPath;
    private string _currentAssetPath;
    // ------------------------------------

    public EditorAppLogic(Editor editor)
    {
        _editor = editor;
        _inspectorPanel = new(_editor);
        EditorTheme.ApplyUnrealEngineStyle();

        // --- Initialize Content Browser Path ---
        _assetRootPath = Path.GetFullPath("Assets");
        _currentAssetPath = _assetRootPath;
        // ---------------------------------------
    }

    public Action<float> DrawUI()
    {
        return (deltaTime) =>
        {
            SetupDockspace();

            DrawOutlinerPanel();
            DrawConsolePanel();
            DrawContentBrowserPanel(); // <-- This is now the new, functional browser
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

    /// <summary>
    /// Draws a navigable file browser for the Assets directory.
    /// </summary>
    private void DrawContentBrowserPanel()
    {
        ImGui.Begin("Content Browser");

        // Back button and current path display
        if (_currentAssetPath != _assetRootPath)
        {
            if (ImGui.Button("<- Back"))
            {
                // Navigate to the parent directory
                _currentAssetPath = Directory.GetParent(_currentAssetPath)?.FullName ?? _assetRootPath;
            }
            ImGui.SameLine();
        }
        ImGui.Text($"Path: {_currentAssetPath.Replace(_assetRootPath, "Assets")}");
        ImGui.Separator();

        // Display subdirectories
        foreach (var directory in Directory.GetDirectories(_currentAssetPath))
        {
            // Use Selectable for folder navigation
            if (ImGui.Selectable($"[F] {Path.GetFileName(directory)}", false, ImGuiSelectableFlags.AllowDoubleClick))
            {
                if (ImGui.IsMouseDoubleClicked(ImGuiMouseButton.Left))
                {
                    _currentAssetPath = directory;
                }
            }
        }

        // Display files
        foreach (var file in Directory.GetFiles(_currentAssetPath))
        {
            string icon = GetIconForFile(file);
            // Future: Implement drag-and-drop from here
            ImGui.Selectable($"{icon} {Path.GetFileName(file)}");
        }

        ImGui.End();
    }

    /// <summary>
    /// Helper to return a simple text "icon" based on file extension.
    /// </summary>
    private string GetIconForFile(string filePath)
    {
        return Path.GetExtension(filePath).ToLowerInvariant() switch
        {
            ".png" or ".jpg" or ".jpeg" or ".tga" => "[T]", // Texture
            ".yaml" or ".scene" => "[S]", // Scene
            ".cs" => "[C#]", // Script
            _ => "[?]" // Unknown
        };
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