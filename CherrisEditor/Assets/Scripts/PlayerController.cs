using Cherris;
using System;
using System.Numerics;

namespace Apexverse
{
    public class PlayerController : Script
    {
        public float Speed { get; set; } = 3.0f;
        public float MouseSensitivity { get; set; } = 0.0015f;

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
            var moveDirection = Vector3.Zero;
            
            if (Input.IsKeyDown(Key.W))
            {
                moveDirection -= Vector3.UnitZ;
            }
            if (Input.IsKeyDown(Key.S))
            {
                moveDirection += Vector3.UnitZ;
            }
            if (Input.IsKeyDown(Key.A))
            {
                moveDirection -= Vector3.UnitX;
            }
            if (Input.IsKeyDown(Key.D))
            {
                moveDirection += Vector3.UnitX;
            }

            if (moveDirection != Vector3.Zero)
            {
                moveDirection = Vector3.Normalize(moveDirection);

                // Create a rotation that only includes the horizontal (yaw) component.
                var yawRotation = Quaternion.CreateFromAxisAngle(Vector3.UnitY, _yaw);

                // Transform the local direction vector by the horizontal-only rotation.
                var worldDirection = Vector3.Transform(moveDirection, yawRotation);
                GameObject.Transform.Position += worldDirection * Speed * deltaTime;
            }
        }
    }
}