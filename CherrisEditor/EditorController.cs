using Cherris;
using System;
using System.Numerics;
using ImGuiNET;

namespace CherrisEditor;

public class EditorController : Script
{
    public float Speed { get; set; } = 5.0f;
    public float MouseSensitivity { get; set; } = 0.0004f;
    public float ZoomSensitivity { get; set; } = 0.5f;

    private float _yaw;
    private float _pitch;

    public override void Start()
    {
        var initialRotation = GameObject.Transform.Rotation;
        var direction = Vector3.Transform(-Vector3.UnitZ, initialRotation);
        _yaw = MathF.Atan2(direction.X, -direction.Z);
        _pitch = MathF.Asin(direction.Y);
    }

    public override void Update(float deltaTime)
    {
        // Don't do anything if an ImGui window has focus
        if (ImGui.GetIO().WantCaptureMouse || ImGui.GetIO().WantCaptureKeyboard)
        {
            // If we were looking around, unlock the mouse
            if (Input.IsMouseLocked)
            {
                Input.IsMouseLocked = false;
            }
            return;
        }

        // --- Mouse Wheel Zoom ---
        if (Input.MouseWheelDelta.Y != 0)
        {
            var forward = Vector3.Transform(-Vector3.UnitZ, GameObject.Transform.Rotation);
            GameObject.Transform.Position += forward * Input.MouseWheelDelta.Y * ZoomSensitivity;
        }

        bool rightMouseDown = Input.IsMouseButtonDown(MouseButton.Right);

        // If the right mouse button is pressed, lock the mouse for looking.
        if (rightMouseDown && !Input.IsMouseLocked)
        {
            Input.IsMouseLocked = true;
        }
        // If the right mouse button is released, unlock.
        else if (!rightMouseDown && Input.IsMouseLocked)
        {
            Input.IsMouseLocked = false;
        }

        if (Input.IsMouseLocked)
        {
            // --- Mouse Look ---
            Vector2 mouseDelta = Input.MouseDelta;
            _yaw -= mouseDelta.X * MouseSensitivity;
            _pitch -= mouseDelta.Y * MouseSensitivity;
            _pitch = Math.Clamp(_pitch, -MathF.PI / 2.0f + 0.001f, MathF.PI / 2.0f - 0.001f);
            GameObject.Transform.Rotation = Quaternion.CreateFromAxisAngle(Vector3.UnitY, _yaw) *
                                              Quaternion.CreateFromAxisAngle(Vector3.UnitX, _pitch);

            // --- Keyboard Movement ---
            float currentSpeed = Speed;
            if (Input.IsKeyDown(Key.ShiftLeft) || Input.IsKeyDown(Key.ShiftRight))
            {
                currentSpeed *= 3.0f; // Speed boost
            }

            var localMove = Vector3.Zero;
            if (Input.IsKeyDown(Key.W)) localMove.Z -= 1;
            if (Input.IsKeyDown(Key.S)) localMove.Z += 1;
            if (Input.IsKeyDown(Key.A)) localMove.X -= 1;
            if (Input.IsKeyDown(Key.D)) localMove.X += 1;

            var worldVerticalMove = 0f;
            if (Input.IsKeyDown(Key.E)) worldVerticalMove += 1; // Up
            if (Input.IsKeyDown(Key.Q)) worldVerticalMove -= 1; // Down

            if (localMove != Vector3.Zero || worldVerticalMove != 0)
            {
                var yawRotation = Quaternion.CreateFromAxisAngle(Vector3.UnitY, _yaw);
                var worldHorizontalMove = Vector3.Transform(localMove, yawRotation);
                var finalMove = new Vector3(worldHorizontalMove.X, worldVerticalMove, worldHorizontalMove.Z);

                if (finalMove.LengthSquared() > 0)
                {
                    GameObject.Transform.Position += Vector3.Normalize(finalMove) * currentSpeed * deltaTime;
                }
            }
        }
    }
}