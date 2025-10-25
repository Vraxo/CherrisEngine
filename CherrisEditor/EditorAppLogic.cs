using Cherris;
using ImGuiNET;
using System.Numerics;
using System;
using ImGuizmoNET;
using System.Runtime.CompilerServices;
using Cherris.OpenTK;
using System.Linq;
using System.Runtime.InteropServices;

namespace CherrisEditor;

public class EditorAppLogic : IDisposable
{
    private readonly Editor _editor;
    private readonly InspectorPanel _inspectorPanel;
    private readonly ContentBrowserPanel _contentBrowserPanel;
    private readonly EditorTextureManager _editorTextureManager;
    private readonly SceneSerializer _sceneSerializer;
    private OPERATION _currentOperation = OPERATION.TRANSLATE;
    private Vector2 _viewportSize = Vector2.Zero;

    public EditorAppLogic(Editor editor)
    {
        _editor = editor;
        _editorTextureManager = new EditorTextureManager();
        _inspectorPanel = new(_editor, _editorTextureManager);
        _contentBrowserPanel = new ContentBrowserPanel(_editorTextureManager);
        _sceneSerializer = new SceneSerializer();
        EditorTheme.ApplyUnrealEngineStyle();

        _editorTextureManager.LoadTexture("Play", "Assets/Icons/play.png");
        _editorTextureManager.LoadTexture("Pause", "Assets/Icons/pause.png");
        _editorTextureManager.LoadTexture("Stop", "Assets/Icons/stop.png");
        _editorTextureManager.LoadTexture("Restart", "Assets/Icons/restart.png");

        ImGuizmo.SetImGuiContext(ImGui.GetCurrentContext());
    }

    public Action<float> DrawUI()
    {
        return (deltaTime) =>
        {
            SetupDockspace();
            ImGuizmo.BeginFrame();

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

        ImGuiWindowFlags windowFlags = ImGuiWindowFlags.NoTitleBar | ImGuiWindowFlags.NoCollapse | ImGuiWindowFlags.NoResize | ImGuiWindowFlags.NoMove | ImGuiWindowFlags.NoBringToFrontOnFocus | ImGuiWindowFlags.NoNavFocus | ImGuiWindowFlags.MenuBar | ImGuiWindowFlags.NoBackground;

        ImGui.Begin("MainDockspace", windowFlags);
        ImGui.PopStyleVar(3);

        DrawMainMenuBar();

        ImGui.PushStyleVar(ImGuiStyleVar.ItemSpacing, new Vector2(ImGui.GetStyle().ItemSpacing.X, 0f));
        DrawToolbar();
        ImGui.PopStyleVar();

        uint dockspaceId = ImGui.GetID("MyDockSpace");
        ImGui.DockSpace(dockspaceId, Vector2.Zero, ImGuiDockNodeFlags.None);

        ImGui.End();
    }

    private void DrawMainMenuBar()
    {
        if (!ImGui.BeginMenuBar()) return;

        if (ImGui.BeginMenu("File"))
        {
            if (ImGui.MenuItem("Save"))
            {
                if (!string.IsNullOrEmpty(_editor.CurrentScenePath))
                {
                    _sceneSerializer.SaveScene(_editor.SceneManager.GameObjects, _editor.CurrentScenePath);
                    Console.WriteLine($"[Editor] Scene saved to '{_editor.CurrentScenePath}'");
                }
            }
            ImGui.Separator();
            if (ImGui.MenuItem("Exit")) { Environment.Exit(0); }
            ImGui.EndMenu();
        }

        ImGui.EndMenuBar();
    }

    private void DrawToolbar()
    {
        float toolbarHeight = ImGui.GetFrameHeightWithSpacing();
        ImGui.BeginChild("ToolbarChild", new Vector2(0, toolbarHeight), false, ImGuiWindowFlags.NoScrollbar | ImGuiWindowFlags.NoScrollWithMouse);

        var style = ImGui.GetStyle();
        float size = ImGui.GetContentRegionAvail().Y;
        float totalWidth = (size * 3) + (style.ItemSpacing.X * 2);
        ImGui.SetCursorPosX((ImGui.GetWindowWidth() * 0.5f) - (totalWidth * 0.5f));

        bool isPlaying = _editor.State == EditorState.Playing;
        bool isEditing = _editor.State == EditorState.Editing;

        IntPtr playPauseIcon = isPlaying ? _editorTextureManager.GetTexture("Pause") : _editorTextureManager.GetTexture("Play");
        if (ImGui.ImageButton("PlayPause", playPauseIcon, new Vector2(size, size)))
        {
            if (isPlaying) _editor.EnterPauseMode();
            else _editor.EnterPlayMode();
        }

        ImGui.SameLine();
        if (isEditing) { ImGui.PushStyleVar(ImGuiStyleVar.Alpha, style.Alpha * 0.5f); ImGui.BeginDisabled(); }
        if (ImGui.ImageButton("Stop", _editorTextureManager.GetTexture("Stop"), new Vector2(size, size))) _editor.EnterEditMode();
        if (isEditing) { ImGui.EndDisabled(); ImGui.PopStyleVar(); }

        ImGui.SameLine();
        if (isEditing) { ImGui.PushStyleVar(ImGuiStyleVar.Alpha, style.Alpha * 0.5f); ImGui.BeginDisabled(); }
        if (ImGui.ImageButton("Restart", _editorTextureManager.GetTexture("Restart"), new Vector2(size, size))) _editor.RestartPlayMode();
        if (isEditing) { ImGui.EndDisabled(); ImGui.PopStyleVar(); }

        ImGui.EndChild();
    }

    private void DrawViewportAndGizmo()
    {
        ImGui.PushStyleVar(ImGuiStyleVar.WindowPadding, Vector2.Zero);
        ImGui.Begin("Viewport");

        bool isViewportHovered = ImGui.IsWindowHovered();
        var currentSize = ImGui.GetContentRegionAvail();

        if (_editor.Renderer is OpenTKRenderer otkRenderer)
        {
            if (currentSize.X > 0 && currentSize.Y > 0 && currentSize != _viewportSize)
            {
                _viewportSize = currentSize;
                otkRenderer.SetViewportSize(_viewportSize);
            }

            IntPtr textureHandle = otkRenderer.GetSceneTextureHandle();
            if (textureHandle != IntPtr.Zero)
            {
                ImGui.Image(textureHandle, _viewportSize, new Vector2(0, 1), new Vector2(1, 0));
            }
        }
        else
        {
            if (currentSize.X > 0 && currentSize.Y > 0)
            {
                _viewportSize = currentSize;
            }
        }

        var viewportPos = ImGui.GetItemRectMin();
        var viewportSize = ImGui.GetItemRectSize();

        if (isViewportHovered && ImGui.IsMouseClicked(ImGuiMouseButton.Left) && !ImGuizmo.IsUsing())
        {
            HandleObjectSelection(ImGui.GetMousePos(), viewportPos, viewportSize);
        }

        DrawGizmo(viewportPos, viewportSize);

        ImGui.End();
        ImGui.PopStyleVar();
    }

    private void DrawGizmo(Vector2 viewportPos, Vector2 viewportSize)
    {
        ImGuizmo.SetRect(viewportPos.X, viewportPos.Y, viewportSize.X, viewportSize.Y);
        ImGuizmo.SetDrawlist();

        GameObject? selectedObject = _editor.GetSelectedGameObject();
        Camera? camera = _editor.SceneManager.MainCamera;

        if (selectedObject != null && camera != null && viewportSize.X > 0 && viewportSize.Y > 0)
        {
            var cameraView = camera.GetViewMatrix();
            var cameraProjection = camera.GetProjectionMatrix(viewportSize.X / viewportSize.Y);
            var objectMatrix = selectedObject.Transform.GetModelMatrix();

            if (ImGuizmo.Manipulate(ref Unsafe.As<Matrix4x4, float>(ref cameraView), ref Unsafe.As<Matrix4x4, float>(ref cameraProjection), _currentOperation, MODE.LOCAL, ref Unsafe.As<Matrix4x4, float>(ref objectMatrix)))
            {
                Matrix4x4.Decompose(objectMatrix, out var scale, out var rotation, out var position);
                selectedObject.Transform.Position = position;
                selectedObject.Transform.Rotation = rotation;
                selectedObject.Transform.Scale = scale;
            }
        }
    }

    private unsafe void DrawOutlinerPanel()
    {
        ImGui.Begin("Outliner");
        DrawOutlinerContextMenu();

        foreach (var go in _editor.SceneManager.GameObjects.Where(g => g.Transform.Parent == null).ToList())
        {
            DrawGameObjectNode(go);
        }

        ImGui.End();
    }

    private unsafe void DrawGameObjectNode(GameObject go)
    {
        ImGuiTreeNodeFlags flags = ImGuiTreeNodeFlags.OpenOnArrow | ImGuiTreeNodeFlags.SpanAvailWidth;
        if (_editor.GetSelectedGameObject() == go) flags |= ImGuiTreeNodeFlags.Selected;
        if (go.Transform.Children.Count == 0) flags |= ImGuiTreeNodeFlags.Leaf;

        bool nodeOpen = ImGui.TreeNodeEx(go.Id.ToString(), flags, go.Name);

        if (ImGui.IsItemClicked()) _editor.SetSelectedGameObject(go);

        if (ImGui.BeginDragDropSource())
        {
            byte[] guidBytes = go.Id.ToByteArray();
            fixed (byte* ptr = guidBytes)
            {
                ImGui.SetDragDropPayload("GAMEOBJECT_ID", (IntPtr)ptr, (uint)guidBytes.Length);
            }
            ImGui.Text(go.Name);
            ImGui.EndDragDropSource();
        }

        if (ImGui.BeginDragDropTarget())
        {
            ImGuiPayloadPtr payload = ImGui.AcceptDragDropPayload("GAMEOBJECT_ID");
            if (payload.NativePtr != null)
            {
                byte[] data = new byte[payload.DataSize];
                Marshal.Copy(payload.Data, data, 0, payload.DataSize);
                var draggedId = new Guid(data);
                GameObject draggedObject = _editor.SceneManager.GameObjects.First(g => g.Id == draggedId);
                draggedObject.Transform.Parent = go.Transform;
            }
            ImGui.EndDragDropTarget();
        }

        if (nodeOpen)
        {
            foreach (var child in go.Transform.Children.ToList())
            {
                DrawGameObjectNode(child.GameObject);
            }
            ImGui.TreePop();
        }
    }

    private void DrawOutlinerContextMenu()
    {
        if (ImGui.BeginPopupContextWindow("OutlinerContextMenu"))
        {
            if (ImGui.MenuItem("Create Empty GameObject"))
            {
                var newGo = new GameObject("New GameObject");
                _editor.SceneManager.AddGameObject(newGo);
                _editor.SetSelectedGameObject(newGo);
            }

            if (_editor.GetSelectedGameObject() != null)
            {
                if (ImGui.MenuItem("Delete", "Del"))
                {
                    _editor.SceneManager.RemoveGameObject(_editor.GetSelectedGameObject());
                    _editor.SetSelectedGameObject(null);
                }
            }
            ImGui.EndPopup();
        }
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
            if (!ImGui.GetIO().WantCaptureKeyboard)
            {
                if (ImGui.IsKeyPressed(ImGuiKey.W)) _currentOperation = OPERATION.TRANSLATE;
                if (ImGui.IsKeyPressed(ImGuiKey.E)) _currentOperation = OPERATION.ROTATE;
                if (ImGui.IsKeyPressed(ImGuiKey.R)) _currentOperation = OPERATION.SCALE;
            }
        }
    }

    private void HandleObjectSelection(Vector2 mousePos, Vector2 viewportPos, Vector2 viewportSize)
    {
        Ray ray = _editor.CreateRayFromViewport(mousePos, viewportPos, viewportSize);
        GameObject? closestObject = null;
        float closestDistance = float.MaxValue;

        foreach (GameObject gameObject in _editor.SceneManager.GameObjects)
        {
            if (gameObject.GetComponent<Skybox>() is not null || gameObject.GetComponent<Camera>() is not null) continue;

            BoundingBox aabb = gameObject.GetWorldSpaceAABB();
            if (!ray.Intersects(aabb, out float distance)) continue;
            if (distance >= closestDistance) continue;

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