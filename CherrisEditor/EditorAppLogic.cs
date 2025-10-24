using Cherris;
using ImGuiNET;
using System.Numerics;
using System;

namespace CherrisEditor;

public class EditorAppLogic : IDisposable
{
    private readonly Editor _editor;
    private readonly InspectorPanel _inspectorPanel;
    private readonly ContentBrowserPanel _contentBrowserPanel;
    private readonly EditorTextureManager _editorTextureManager;
    private readonly SceneSerializer _sceneSerializer;

    public EditorAppLogic(Editor editor)
    {
        _editor = editor;
        _editorTextureManager = new EditorTextureManager();
        _inspectorPanel = new(_editor, _editorTextureManager);
        _contentBrowserPanel = new ContentBrowserPanel(_editorTextureManager);
        _sceneSerializer = new SceneSerializer();
        EditorTheme.ApplyUnrealEngineStyle();
    }

    public Action<float> DrawUI()
    {
        return (deltaTime) =>
        {
            // The toolbar is now drawn inside SetupDockspace to ensure correct layout
            SetupDockspace();

            // These panels will be docked within the space created above
            DrawOutlinerPanel();
            DrawConsolePanel();
            _contentBrowserPanel.Draw();
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

        DrawMainMenuBar();

        // A child window is an item. The parent's ItemSpacing.Y is applied after it,
        // creating a gap. We remove this vertical spacing just for the toolbar.
        ImGui.PushStyleVar(ImGuiStyleVar.ItemSpacing, new Vector2(ImGui.GetStyle().ItemSpacing.X, 0f));
        DrawToolbar();
        ImGui.PopStyleVar();

        // Create the area where other windows can be docked
        uint dockspaceId = ImGui.GetID("MyDockSpace");
        ImGui.DockSpace(dockspaceId, Vector2.Zero, ImGuiDockNodeFlags.PassthruCentralNode);

        ImGui.End();
    }

    private void DrawMainMenuBar()
    {
        if (!ImGui.BeginMenuBar()) return;

        if (ImGui.BeginMenu("File"))
        {
            if (ImGui.MenuItem("New Scene")) { }
            if (ImGui.MenuItem("Open Scene")) { }
            ImGui.Separator();
            if (ImGui.MenuItem("Save"))
            {
                if (!string.IsNullOrEmpty(_editor.CurrentScenePath))
                {
                    _sceneSerializer.SaveScene(_editor.SceneManager.GameObjects, _editor.CurrentScenePath);
                    Console.WriteLine($"[Editor] Scene saved to '{_editor.CurrentScenePath}'");
                }
                else
                {
                    Console.WriteLine("[Editor] No scene path set. Use 'Save As...' first.");
                }
            }
            if (ImGui.MenuItem("Save As..."))
            {
                Console.WriteLine("[Editor] 'Save As...' is not implemented yet.");
            }
            ImGui.Separator();
            if (ImGui.MenuItem("Exit")) { Console.WriteLine("Exit clicked!"); }
            ImGui.EndMenu();
        }

        ImGui.EndMenuBar();
    }

    private void DrawToolbar()
    {
        // Use a child window to create a distinct bar area.
        float toolbarHeight = ImGui.GetFrameHeightWithSpacing();
        ImGui.BeginChild("ToolbarChild", new Vector2(0, toolbarHeight), ImGuiChildFlags.None, ImGuiWindowFlags.NoScrollbar | ImGuiWindowFlags.NoScrollWithMouse);

        var style = ImGui.GetStyle();
        float size = ImGui.GetContentRegionAvail().Y;

        // The group of three buttons is always present and centered for a stable layout
        float totalWidth = (size * 3) + (style.ItemSpacing.X * 2);
        ImGui.SetCursorPosX((ImGui.GetWindowWidth() * 0.5f) - (totalWidth * 0.5f));

        bool isPlaying = _editor.State == EditorState.Playing;
        bool isEditing = _editor.State == EditorState.Editing;

        // --- Play / Pause Button ---
        string playPauseText = isPlaying ? "Pause" : "Play";
        if (ImGui.Button(playPauseText, new Vector2(size, size)))
        {
            if (isPlaying)
            {
                _editor.Pause();
            }
            else // State is Editing or Paused, both should trigger Play/Resume
            {
                _editor.Play();
            }
        }

        ImGui.SameLine();

        // --- Stop Button ---
        if (isEditing)
        {
            ImGui.PushStyleVar(ImGuiStyleVar.Alpha, style.Alpha * 0.5f);
            ImGui.BeginDisabled();
        }

        if (ImGui.Button("Stop", new Vector2(size, size)))
        {
            _editor.Stop();
        }

        if (isEditing)
        {
            ImGui.EndDisabled();
            ImGui.PopStyleVar();
        }

        ImGui.SameLine();

        // --- Restart Button ---
        if (isEditing)
        {
            ImGui.PushStyleVar(ImGuiStyleVar.Alpha, style.Alpha * 0.5f);
            ImGui.BeginDisabled();
        }

        if (ImGui.Button("Restart", new Vector2(size, size)))
        {
            _editor.Restart();
        }

        if (isEditing)
        {
            ImGui.EndDisabled();
            ImGui.PopStyleVar();
        }

        ImGui.EndChild();
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

    public void UpdateEditorLogic(float deltaTime)
    {
        // Only allow object selection when not in play mode
        if (_editor.State == EditorState.Editing)
        {
            HandleObjectSelection();
            HandleMovement(deltaTime);
        }
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
    }

    private void HandleMovement(float deltaTime)
    {
        GameObject? selectedGameObject = _editor.GetSelectedGameObject();
        if (selectedGameObject is null) return;

        const float moveSpeed = 2.0f;
        Vector3 moveDirection = Vector3.Zero;
        bool shiftHeld = Input.IsKeyDown(Key.ShiftLeft) || Input.IsKeyDown(Key.ShiftRight);

        if (Input.IsKeyDown(Key.Left)) moveDirection.X -= 1;
        if (Input.IsKeyDown(Key.Right)) moveDirection.X += 1;

        if (shiftHeld)
        {
            if (Input.IsKeyDown(Key.Up)) moveDirection.Y += 1;
            if (Input.IsKeyDown(Key.Down)) moveDirection.Y -= 1;
        }
        else
        {
            if (Input.IsKeyDown(Key.Up)) moveDirection.Z -= 1;
            if (Input.IsKeyDown(Key.Down)) moveDirection.Z += 1;
        }

        if (moveDirection != Vector3.Zero)
        {
            selectedGameObject.Transform.Position += Vector3.Normalize(moveDirection) * moveSpeed * deltaTime;
        }
    }

    public void Dispose()
    {
        _contentBrowserPanel.Dispose();
        _editorTextureManager.Dispose();
    }
}