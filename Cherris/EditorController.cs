using Cherris;
using System;
using System.Numerics;
using Veldrid;

namespace Apexverse;

public class EditorController : Script
{
    public float Speed { get; set; } = 5.0f;
    public float MouseSensitivity { get; set; } = 0.0015f;

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
        var moveDirection = Vector3.Zero;

        if (Input.IsKeyDown(Key.W)) moveDirection -= Vector3.UnitZ;
        if (Input.IsKeyDown(Key.S)) moveDirection += Vector3.UnitZ;
        if (Input.IsKeyDown(Key.A)) moveDirection -= Vector3.UnitX;
        if (Input.IsKeyDown(Key.D)) moveDirection += Vector3.UnitX;
        if (Input.IsKeyDown(Key.E)) moveDirection += Vector3.UnitY; // Up
        if (Input.IsKeyDown(Key.Q)) moveDirection -= Vector3.UnitY; // Down

        if (moveDirection != Vector3.Zero)
        {
            moveDirection = Vector3.Normalize(moveDirection);
            // Transform the local direction vector by the full camera rotation to fly.
            var worldDirection = Vector3.Transform(moveDirection, GameObject.Transform.Rotation);
            GameObject.Transform.Position += worldDirection * Speed * deltaTime;
        }
    }
}