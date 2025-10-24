using Cherris;
using ImGuiNET;
using System.Numerics;
using System;
using ImGuizmoNET;
using System.Runtime.CompilerServices;

namespace CherrisEditor;

public class EditorAppLogic : IDisposable
{
    private readonly Editor _editor;
    private readonly InspectorPanel _inspectorPanel;
    private readonly ContentBrowserPanel _contentBrowserPanel;
    private readonly EditorTextureManager _editorTextureManager;
    private readonly SceneSerializer _sceneSerializer;
    private OPERATION _currentOperation = OPERATION.TRANSLATE;

    public EditorAppLogic(Editor editor)
    {
        _editor = editor;
        _editorTextureManager = new EditorTextureManager();
        _inspectorPanel = new(_editor, _editorTextureManager);
        _contentBrowserPanel = new ContentBrowserPanel(_editorTextureManager);
        _sceneSerializer = new SceneSerializer();
        EditorTheme.ApplyUnrealEngineStyle();

        // Load the new icons needed for the toolbar
        _editorTextureManager.LoadTexture("Play", "Assets/Icons/play.png");
        _editorTextureManager.LoadTexture("Pause", "Assets/Icons/pause.png");
        _editorTextureManager.LoadTexture("Stop", "Assets/Icons/stop.png");
        _editorTextureManager.LoadTexture("Restart", "Assets/Icons/restart.png");

        // Initialize ImGuizmo
        ImGuizmo.SetImGuiContext(ImGui.GetCurrentContext());
    }

    public Action<float> DrawUI()
    {
        return (deltaTime) =>
        {
            SetupDockspace();

            // Set up ImGuizmo for the new frame
            ImGuizmo.BeginFrame();

            // Draw all editor panels
            DrawViewportAndGizmo();
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

        ImGui.PushStyleVar(ImGuiStyleVar.ItemSpacing, new Vector2(ImGui.GetStyle().ItemSpacing.X, 0f));
        DrawToolbar();
        ImGui.PopStyleVar();

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
        float toolbarHeight = ImGui.GetFrameHeightWithSpacing();
        ImGui.BeginChild("ToolbarChild", new Vector2(0, toolbarHeight), ImGuiChildFlags.None, ImGuiWindowFlags.NoScrollbar | ImGuiWindowFlags.NoScrollWithMouse);

        var style = ImGui.GetStyle();
        float size = ImGui.GetContentRegionAvail().Y;

        float totalWidth = (size * 3) + (style.ItemSpacing.X * 2);
        ImGui.SetCursorPosX((ImGui.GetWindowWidth() * 0.5f) - (totalWidth * 0.5f));

        bool isPlaying = _editor.State == EditorState.Playing;
        bool isEditing = _editor.State == EditorState.Editing;

        IntPtr playPauseIcon = isPlaying ? _editorTextureManager.GetTexture("Pause") : _editorTextureManager.GetTexture("Play");
        if (ImGui.ImageButton("PlayPause", playPauseIcon, new Vector2(size, size)))
        {
            if (isPlaying)
            {
                _editor.Pause();
            }
            else
            {
                _editor.Play();
            }
        }

        ImGui.SameLine();

        if (isEditing)
        {
            ImGui.PushStyleVar(ImGuiStyleVar.Alpha, style.Alpha * 0.5f);
            ImGui.BeginDisabled();
        }

        IntPtr stopIcon = _editorTextureManager.GetTexture("Stop");
        if (ImGui.ImageButton("Stop", stopIcon, new Vector2(size, size)))
        {
            _editor.Stop();
        }

        if (isEditing)
        {
            ImGui.EndDisabled();
            ImGui.PopStyleVar();
        }

        ImGui.SameLine();

        if (isEditing)
        {
            ImGui.PushStyleVar(ImGuiStyleVar.Alpha, style.Alpha * 0.5f);
            ImGui.BeginDisabled();
        }

        IntPtr restartIcon = _editorTextureManager.GetTexture("Restart");
        if (ImGui.ImageButton("Restart", restartIcon, new Vector2(size, size)))
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

    private void DrawViewportAndGizmo()
    {
        ImGui.PushStyleVar(ImGuiStyleVar.WindowPadding, Vector2.Zero);
        ImGui.Begin("Viewport", ImGuiWindowFlags.NoBackground);

        // Get the viewport boundaries to feed to ImGuizmo
        var viewportPos = ImGui.GetCursorScreenPos();
        var viewportSize = ImGui.GetContentRegionAvail();
        ImGuizmo.SetRect(viewportPos.X, viewportPos.Y, viewportSize.X, viewportSize.Y);

        // CRITICAL FIX: Set the draw list for ImGuizmo to render onto.
        ImGuizmo.SetDrawlist();

        GameObject? selectedObject = _editor.GetSelectedGameObject();
        Camera? camera = _editor.SceneManager.MainCamera;

        if (selectedObject != null && camera != null && viewportSize.X > 0 && viewportSize.Y > 0)
        {
            var cameraView = camera.GetViewMatrix();
            var cameraProjection = camera.GetProjectionMatrix(viewportSize.X / viewportSize.Y);
            var objectMatrix = selectedObject.Transform.GetModelMatrix();

            // Use Unsafe.As to pass a ref float from our matrix structs, which is what the C++ backend expects
            if (ImGuizmo.Manipulate(
                ref Unsafe.As<Matrix4x4, float>(ref cameraView),
                ref Unsafe.As<Matrix4x4, float>(ref cameraProjection),
                _currentOperation,
                MODE.LOCAL,
                ref Unsafe.As<Matrix4x4, float>(ref objectMatrix)))
            {
                Matrix4x4.Decompose(objectMatrix, out var scale, out var rotation, out var position);
                selectedObject.Transform.Position = position;
                selectedObject.Transform.Rotation = rotation;
                selectedObject.Transform.Scale = scale;
            }
        }

        ImGui.End();
        ImGui.PopStyleVar();
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
        if (_editor.State == EditorState.Editing)
        {
            // Do not use gizmo movement keys if ImGui is using the keyboard
            if (!ImGui.GetIO().WantCaptureKeyboard)
            {
                if (Input.WasKeyPressed(Key.W)) _currentOperation = OPERATION.TRANSLATE;
                if (Input.WasKeyPressed(Key.E)) _currentOperation = OPERATION.ROTATE;
                if (Input.WasKeyPressed(Key.R)) _currentOperation = OPERATION.SCALE;
            }

            // Object selection should only happen if the gizmo is not being used
            if (!ImGuizmo.IsUsing())
            {
                HandleObjectSelection();
            }
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

    public void Dispose()
    {
        _contentBrowserPanel.Dispose();
        _editorTextureManager.Dispose();
    }
}