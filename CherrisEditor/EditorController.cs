using Cherris;
using ImGuizmoNET;
using System;
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

    public EditorController(Editor editor)
    {
        _editor = editor;
    }

    /// <summary>
    /// Initializes the internal yaw and pitch from the GameObject's current rotation.
    /// This should only be called once at the start, or after an external rotation (e.g., from a gizmo).
    /// Calling this frequently can lead to instabilities due to Euler angle ambiguities.
    /// </summary>
    public void SyncYawPitchFromTransform()
    {
        Quaternion q = GameObject.Transform.Rotation;
        Vector3 forward = GameObject.Transform.Forward;

        // Pitch is the angle of the forward vector with the horizontal XZ plane.
        _pitch = MathF.Asin(-forward.Y);

        // Yaw is the angle of the forward vector's projection onto the XZ plane.
        _yaw = MathF.Atan2(forward.X, -forward.Z);

        Console.WriteLine($"[Controller] Syncing orientation. New Yaw/Pitch: <{_yaw}, {_pitch}>. From Rotation: {q}");
    }

    public override void Start()
    {
        SyncYawPitchFromTransform();
    }

    public override void Update(float deltaTime)
    {
        if (!_editor.IsViewportHovered || ImGuizmo.IsUsing())
        {
            return;
        }

        HandleInput(deltaTime);
    }

    private void HandleInput(float deltaTime)
    {
        HandleMouseZoom();
        HandleKeyboardMovement(deltaTime);
        HandleMouseLook();
    }

    private void HandleMouseZoom()
    {
        float mouseWheelDelta = Input.MouseWheelDelta.Y;
        if (mouseWheelDelta == 0)
        {
            return;
        }

        GameObject.Transform.Position += GameObject.Transform.Forward * mouseWheelDelta * ZoomSensitivity;
    }

    private void HandleKeyboardMovement(float deltaTime)
    {
        float currentSpeed = Speed;
        if (Input.IsKeyDown(Key.ShiftLeft) || Input.IsKeyDown(Key.ShiftRight))
        {
            currentSpeed *= 3.0f; // Speed boost
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
            worldVerticalMove += 1; // Up
        }
        if (Input.IsKeyDown(Key.Q))
        {
            worldVerticalMove -= 1; // Down
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

        // We no longer sync on click. We just apply the delta to the existing yaw/pitch.
        // This prevents the jump caused by mathematical ambiguity in Euler angle conversion.
        Vector2 mouseDelta = Input.MouseDelta;
        if (mouseDelta == Vector2.Zero) return;

        Console.WriteLine($"[Controller] HandleMouseLook: Panning with delta <{mouseDelta.X}, {mouseDelta.Y}>");

        _yaw -= mouseDelta.X * MouseSensitivity;
        _pitch -= mouseDelta.Y * MouseSensitivity;
        _pitch = Math.Clamp(_pitch, -MathF.PI / 2.0f + 0.001f, MathF.PI / 2.0f - 0.001f);

        var newRotation = Quaternion.CreateFromAxisAngle(Vector3.UnitY, _yaw) *
                          Quaternion.CreateFromAxisAngle(Vector3.UnitX, _pitch);

        Console.WriteLine($"[Controller] Old Rotation: {GameObject.Transform.Rotation}, New Rotation: {newRotation}");
        GameObject.Transform.Rotation = newRotation;
    }
}