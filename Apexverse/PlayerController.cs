using Cherris;
using System;
using System.Numerics;

namespace Apexverse
{
    public class PlayerController : Script
    {
        public float Speed { get; set; } = 3.0f;
        public float MouseSensitivity { get; set; } = 0.00015f;

        private float _yaw;
        private float _pitch;

        public override void Update(float deltaTime)
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

            // --- Keyboard Movement ---
            var moveInput = Vector3.Zero;

            if (Input.IsKeyDown(Key.W))
            {
                moveInput.Z -= 1f;
            }
            if (Input.IsKeyDown(Key.S))
            {
                moveInput.Z += 1f;
            }
            if (Input.IsKeyDown(Key.A))
            {
                moveInput.X -= 1f;
            }
            if (Input.IsKeyDown(Key.D))
            {
                moveInput.X += 1f;
            }

            if (moveInput.LengthSquared() > 0)
            {
                // Create a rotation that only includes the horizontal (yaw) component.
                var yawRotation = Quaternion.CreateFromAxisAngle(Vector3.UnitY, _yaw);

                // Transform the local input vector by the horizontal-only rotation.
                // This ensures movement is on the XZ plane regardless of camera pitch.
                var worldDirection = Vector3.Transform(moveInput, yawRotation);

                // Normalize the final world direction to ensure consistent speed.
                worldDirection = Vector3.Normalize(worldDirection);
                GameObject.Transform.Position += worldDirection * Speed * deltaTime;
            }
        }
    }
}