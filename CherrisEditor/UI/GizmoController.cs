using Cherris.Core;
using CherrisEditor.Undo;
using CherrisEditor.Undo.Commands;
using ImGuiNET;
using ImGuizmoNET;
using System.Numerics;
using System.Runtime.CompilerServices;

namespace CherrisEditor.UI;

public class GizmoController
{
    private readonly Editor _editor;
    private readonly HistoryManager _history;

    private OPERATION _currentOperation = OPERATION.TRANSLATE;
    private bool _isManipulating;
    private Vector3 _initialPosition;
    private Quaternion _initialRotation;
    private Vector3 _initialScale;

    public GizmoController(Editor editor, HistoryManager history)
    {
        _editor = editor;
        _history = history;
    }

    public void Update()
    {
        if (_editor.State != EditorState.Editing || ImGuiNET.ImGui.GetIO().WantCaptureKeyboard)
        {
            return;
        }

        if (ImGuiNET.ImGui.IsKeyPressed(ImGuiKey.W))
        {
            _currentOperation = OPERATION.TRANSLATE;
        }

        if (ImGuiNET.ImGui.IsKeyPressed(ImGuiKey.E))
        {
            _currentOperation = OPERATION.ROTATE;
        }

        if (ImGuiNET.ImGui.IsKeyPressed(ImGuiKey.R))
        {
            _currentOperation = OPERATION.SCALE;
        }
    }

    public void Draw(Vector2 viewportPos, Vector2 viewportSize)
    {
        var selectedObject = _editor.GetSelectedGameObject();
        var camera = _editor.SceneManager.MainCamera;

        if (selectedObject is null || camera is null || viewportSize.X <= 0 || viewportSize.Y <= 0)
        {
            return;
        }

        ImGuizmo.SetRect(viewportPos.X, viewportPos.Y, viewportSize.X, viewportSize.Y);
        ImGuizmo.SetDrawlist();

        var view = camera.GetViewMatrix();
        var projection = camera.GetProjectionMatrix(viewportSize.X / viewportSize.Y);
        var objectMatrix = selectedObject.Transform.GetModelMatrix();

        CaptureInitialStateIfStarting(selectedObject);

        if (ImGuizmo.Manipulate(
            ref AsFloat(ref view),
            ref AsFloat(ref projection),
            _currentOperation,
            MODE.LOCAL,
            ref AsFloat(ref objectMatrix)))
        {
            ApplyDecomposedTransform(selectedObject, objectMatrix);
        }

        RecordHistoryIfFinished(selectedObject);
    }

    private void CaptureInitialStateIfStarting(GameObject selected)
    {
        if (!ImGuizmo.IsUsing() || _isManipulating)
        {
            return;
        }

        _isManipulating = true;
        _initialPosition = selected.Transform.Position;
        _initialRotation = selected.Transform.Rotation;
        _initialScale = selected.Transform.Scale;
    }

    private void RecordHistoryIfFinished(GameObject selected)
    {
        if (ImGuizmo.IsUsing() || !_isManipulating)
        {
            return;
        }

        _isManipulating = false;

        bool unchanged =
            selected.Transform.Position == _initialPosition &&
            selected.Transform.Rotation == _initialRotation &&
            selected.Transform.Scale == _initialScale;

        if (unchanged)
        {
            return;
        }

        var newPosition = selected.Transform.Position;
        var newRotation = selected.Transform.Rotation;
        var newScale = selected.Transform.Scale;

        RevertTarget(selected);

        _history.Execute(new ChangeTransformCommand(
            selected.Transform,
            _initialPosition, _initialRotation, _initialScale,
            newPosition, newRotation, newScale));
    }

    private void RevertTarget(GameObject target)
    {
        target.Transform.Position = _initialPosition;
        target.Transform.Rotation = _initialRotation;
        target.Transform.Scale = _initialScale;
    }

    private static void ApplyDecomposedTransform(GameObject target, Matrix4x4 matrix)
    {
        Matrix4x4.Decompose(matrix, out var scale, out var rotation, out var position);
        target.Transform.Position = position;
        target.Transform.Rotation = rotation;
        target.Transform.Scale = scale;
    }

    private static ref float AsFloat(ref Matrix4x4 matrix)
    {
        return ref Unsafe.As<Matrix4x4, float>(ref matrix);
    }
}