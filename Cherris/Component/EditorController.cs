using Cherris;
using System;
using System.Numerics;
using Veldrid;

namespace Apexverse;

public class EditorController : Script
{
    public float Speed { get; set; } = 5.0f;
    public float MouseSensitivity { get; set; } = 0.00015f;

    private float _yaw;
    private float _pitch;

    public override void Start()
    {
        // Initialize yaw and pitch from the camera's starting rotation
        var initialRotation = GameObject.Transform.Rotation;
        var direction = Vector3.Transform(-Vector3.UnitZ, initialRotation);
        _yaw = MathF.Atan2(direction.X, -direction.Z);
        _pitch = MathF.Asin(direction.Y);
    }

    public override void Update(float deltaTime)
    {
        if (Input.IsMouseLocked)
        {
            // --- Mouse Look ---
            Vector2 mouseDelta = Input.MouseDelta;
            _yaw -= mouseDelta.X * MouseSensitivity;
            _pitch -= mouseDelta.Y * MouseSensitivity;

            // Clamp the pitch to prevent the camera from flipping upside down
            _pitch = Math.Clamp(_pitch, -MathF.PI / 2.0f + 0.001f, MathF.PI / 2.0f - 0.001f);

            // Update rotation from yaw and pitch
            GameObject.Transform.Rotation = Quaternion.CreateFromAxisAngle(Vector3.UnitY, _yaw) *
                                              Quaternion.CreateFromAxisAngle(Vector3.UnitX, _pitch);
        }

        // --- Keyboard Movement ---
        var localMove = Vector3.Zero;
        if (Input.IsKeyDown(Key.W)) localMove.Z -= 1;
        if (Input.IsKeyDown(Key.S)) localMove.Z += 1;
        if (Input.IsKeyDown(Key.A)) localMove.X -= 1;
        if (Input.IsKeyDown(Key.D)) localMove.X += 1;

        var worldVerticalMove = 0f;
        if (Input.IsKeyDown(Key.Space)) worldVerticalMove += 1;
        if (Input.IsKeyDown(Key.ShiftLeft) || Input.IsKeyDown(Key.ShiftRight)) worldVerticalMove -= 1;

        if (localMove != Vector3.Zero || worldVerticalMove != 0)
        {
            // Create a rotation that only includes the horizontal (yaw) component.
            var yawRotation = Quaternion.CreateFromAxisAngle(Vector3.UnitY, _yaw);

            // Transform local horizontal movement into world space.
            var worldHorizontalMove = Vector3.Transform(localMove, yawRotation);

            // Combine with world vertical movement.
            var finalMove = new Vector3(worldHorizontalMove.X, worldVerticalMove, worldHorizontalMove.Z);

            // Normalize the final vector to ensure consistent speed in all directions.
            if (finalMove.LengthSquared() > 0)
            {
                GameObject.Transform.Position += Vector3.Normalize(finalMove) * Speed * deltaTime;
            }
        }
    }
}