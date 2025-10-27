using Cherris;
using CherrisEditor.Undo;
using CherrisEditor.Undo.Commands;
using ImGuiNET;
using ImGuizmoNET;
using System.Numerics;
using System.Runtime.CompilerServices;

namespace CherrisEditor.UI;

public class ViewportPanel
{
    private readonly Editor _editor;
    private readonly HistoryManager _history;
    private OPERATION _currentOperation = OPERATION.TRANSLATE;
    private Vector2 _viewportSize = Vector2.Zero;

    // For gizmo undo
    private bool _isManipulatingGizmo;
    private Vector3 _initialPosition;
    private Quaternion _initialRotation;
    private Vector3 _initialScale;

    public ViewportPanel(Editor editor, HistoryManager history)
    {
        _editor = editor;
        _history = history;
    }

    public void Update()
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

    public void Draw()
    {
        ImGui.PushStyleVar(ImGuiStyleVar.WindowPadding, Vector2.Zero);
        ImGui.Begin("Viewport", ImGuiWindowFlags.NoScrollbar | ImGuiWindowFlags.NoScrollWithMouse);

        var openScenes = _editor.SceneManager.OpenScenes.ToList();
        var activeScene = _editor.SceneManager.ActiveScene;

        // Default the viewport to not hovered. It will be set to true only if the image within the active tab is hovered.
        _editor.IsViewportHovered = false;

        if (ImGui.BeginTabBar("SceneTabBar", ImGuiTabBarFlags.Reorderable))
        {
            if (openScenes.Count == 0)
            {
                if (ImGui.BeginTabItem("No Scene"))
                {
                    ImGui.Text("No scene loaded. Open a scene from the Content Browser.");
                    ImGui.EndTabItem();
                }
            }

            foreach (var scene in openScenes)
            {
                bool isOpen = true;
                ImGuiTabItemFlags flags = scene.IsDirty ? ImGuiTabItemFlags.UnsavedDocument : ImGuiTabItemFlags.None;
                if (scene == activeScene)
                {
                    flags |= ImGuiTabItemFlags.SetSelected;
                }

                if (ImGui.BeginTabItem(scene.Name, ref isOpen, flags))
                {
                    if (ImGui.IsItemClicked(ImGuiMouseButton.Left))
                    {
                        _editor.SceneManager.SetActiveScene(scene);
                    }

                    // Only render the viewport content for the currently active scene
                    if (scene == activeScene)
                    {
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

                        _editor.IsViewportHovered = ImGui.IsItemHovered();

                        var viewportPos = ImGui.GetItemRectMin();
                        var viewportSize = ImGui.GetItemRectSize();

                        if (_editor.IsViewportHovered && ImGui.IsMouseClicked(ImGuiMouseButton.Left) && !ImGuizmo.IsUsing() && !ImGuizmo.IsOver())
                        {
                            HandleObjectSelection(ImGui.GetMousePos(), viewportPos, viewportSize);
                        }

                        DrawGizmo(viewportPos, viewportSize);
                    }

                    ImGui.EndTabItem();
                }

                if (!isOpen)
                {
                    // TODO: Prompt to save if dirty
                    _editor.SceneManager.CloseScene(scene);
                }
            }
            ImGui.EndTabBar();
        }

        ImGui.End();
        ImGui.PopStyleVar();
    }

    private void DrawGizmo(Vector2 viewportPos, Vector2 viewportSize)
    {
        ImGuizmo.SetRect(viewportPos.X, viewportPos.Y, viewportSize.X, viewportSize.Y);
        ImGuizmo.SetDrawlist();

        GameObject? selectedObject = _editor.GetSelectedGameObject();
        Camera? camera = _editor.SceneManager.MainCamera;

        if (selectedObject is not null && camera is not null && viewportSize.X > 0 && viewportSize.Y > 0)
        {
            var cameraView = camera.GetViewMatrix();
            var cameraProjection = camera.GetProjectionMatrix(viewportSize.X / viewportSize.Y);
            var objectMatrix = selectedObject.Transform.GetModelMatrix();

            // Capture initial state when manipulation starts
            if (ImGuizmo.IsUsing() && !_isManipulatingGizmo)
            {
                _isManipulatingGizmo = true;
                _initialPosition = selectedObject.Transform.Position;
                _initialRotation = selectedObject.Transform.Rotation;
                _initialScale = selectedObject.Transform.Scale;
            }

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

            // Create command when manipulation ends
            if (!ImGuizmo.IsUsing() && _isManipulatingGizmo)
            {
                _isManipulatingGizmo = false;
                var newPosition = selectedObject.Transform.Position;
                var newRotation = selectedObject.Transform.Rotation;
                var newScale = selectedObject.Transform.Scale;

                // Only create command if something actually changed
                if (newPosition != _initialPosition || newRotation != _initialRotation || newScale != _initialScale)
                {
                    // Revert the change so the command can apply it
                    selectedObject.Transform.Position = _initialPosition;
                    selectedObject.Transform.Rotation = _initialRotation;
                    selectedObject.Transform.Scale = _initialScale;

                    var command = new ChangeTransformCommand(
                        selectedObject.Transform,
                        _initialPosition, _initialRotation, _initialScale,
                        newPosition, newRotation, newScale
                    );
                    _history.Execute(command);
                }
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
}