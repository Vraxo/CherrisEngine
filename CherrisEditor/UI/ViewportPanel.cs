using Cherris;
using Cherris.OpenTK;
using ImGuiNET;
using ImGuizmoNET;
using System;
using System.Numerics;
using System.Runtime.CompilerServices;

namespace CherrisEditor.UI;

public class ViewportPanel
{
    private readonly Editor _editor;
    private OPERATION _currentOperation = OPERATION.TRANSLATE;
    private Vector2 _viewportSize = Vector2.Zero;

    public ViewportPanel(Editor editor)
    {
        _editor = editor;
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

        if (selectedObject is not null && camera is not null && viewportSize.X > 0 && viewportSize.Y > 0)
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