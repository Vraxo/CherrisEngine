using Cherris;
using Cherris.Components;
using ImGuizmoNET;
using System.Numerics;

namespace CherrisEditor;

public class EditorController : Script
{
    public float Speed { get; set; } = 5.0f;
    public float MouseSensitivity { get; set; } = 0.0015f;
    public float ZoomSensitivity { get; set; } = 0.5f;

    private float _yaw;
    private float _pitch;
    private readonly Editor _editor;
    private bool _isRotating;

    public EditorController(Editor editor)
    {
        _editor = editor;
    }

    private void SyncYawPitchFromTransform()
    {
        var eulerAngles = EngineMath.ToEulerAngles(GameObject.Transform.Rotation);
        _pitch = eulerAngles.X;
        _yaw = eulerAngles.Y;
    }

    public override void Start()
    {
        SyncYawPitchFromTransform();
    }

    public override void Update(float deltaTime)
    {
        if (ImGuizmo.IsUsing())
        {
            return;
        }

        // Manage camera rotation drag state
        if (_isRotating && !Input.IsMouseButtonDown(MouseButton.Right))
        {
            _isRotating = false;
        }

        if (!_isRotating && Input.WasMouseButtonPressed(MouseButton.Right) && _editor.IsViewportHovered)
        {
            _isRotating = true;
            SyncYawPitchFromTransform();
        }

        bool viewportHovered = _editor.IsViewportHovered;

        // Keyboard and Zoom only work when viewport is hovered
        if (viewportHovered)
        {
            HandleMouseZoom();
            HandleKeyboardMovement(deltaTime);
        }

        // Mouse look works when hovering OR when dragging started inside
        if (viewportHovered || _isRotating)
        {
            HandleMouseLook();
        }
    }

    private void HandleMouseZoom()
    {
        float mouseWheelDelta = Input.MouseWheelDelta.Y;
        if (mouseWheelDelta == 0)
        {
            return;
        }

        Vector3 forward = Vector3.Transform(-Vector3.UnitZ, GameObject.Transform.Rotation);
        GameObject.Transform.Position += forward * mouseWheelDelta * ZoomSensitivity;
    }

    private void HandleKeyboardMovement(float deltaTime)
    {
        float currentSpeed = Speed;
        if (Input.IsKeyDown(Key.ShiftLeft) || Input.IsKeyDown(Key.ShiftRight))
        {
            currentSpeed *= 3.0f;
        }

        var localMove = Vector3.Zero;

        if (Input.IsKeyDown(Key.W))
        {
            localMove.Z -= 1;
        }

        if (Input.IsKeyDown(Key.S))
        {
            localMove.Z += 1;
        }

        if (Input.IsKeyDown(Key.A))
        {
            localMove.X -= 1;
        }

        if (Input.IsKeyDown(Key.D))
        {
            localMove.X += 1;
        }

        float worldVerticalMove = 0f;
        if (Input.IsKeyDown(Key.E))
        {
            worldVerticalMove += 1;
        }

        if (Input.IsKeyDown(Key.Q))
        {
            worldVerticalMove -= 1;
        }

        if (localMove == Vector3.Zero && worldVerticalMove == 0)
        {
            return;
        }

        Quaternion yawRotation = Quaternion.CreateFromAxisAngle(Vector3.UnitY, _yaw);
        Vector3 worldHorizontalMove = Vector3.Transform(localMove, yawRotation);
        var finalMove = new Vector3(worldHorizontalMove.X, worldVerticalMove, worldHorizontalMove.Z);

        if (finalMove.LengthSquared() > 0)
        {
            GameObject.Transform.Position += Vector3.Normalize(finalMove) * currentSpeed * deltaTime;
        }
    }

    private void HandleMouseLook()
    {
        if (!Input.IsMouseButtonDown(MouseButton.Right))
        {
            return;
        }

        Vector2 mouseDelta = Input.MouseDelta;
        if (mouseDelta == Vector2.Zero)
        {
            return;
        }

        _yaw -= mouseDelta.X * MouseSensitivity;
        _pitch -= mouseDelta.Y * MouseSensitivity;
        _pitch = Math.Clamp(_pitch, (-MathF.PI / 2.0f) + 0.001f, (MathF.PI / 2.0f) - 0.001f);

        GameObject.Transform.Rotation = Quaternion.CreateFromYawPitchRoll(_yaw, _pitch, 0);
    }
}