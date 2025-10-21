using Cherris;
using System;
using System.Numerics;

namespace Apexverse;

public class EditorController : Script
{
    public float Speed { get; set; } = 5.0f;
    public float MouseSensitivity { get; set; } = 0.00015f;

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
        if (Input.IsMouseLocked)
        {
            Vector2 mouseDelta = Input.MouseDelta;
            _yaw -= mouseDelta.X * MouseSensitivity;
            _pitch -= mouseDelta.Y * MouseSensitivity;
            _pitch = Math.Clamp(_pitch, -MathF.PI / 2.0f + 0.001f, MathF.PI / 2.0f - 0.001f);
            GameObject.Transform.Rotation = Quaternion.CreateFromAxisAngle(Vector3.UnitY, _yaw) *
                                              Quaternion.CreateFromAxisAngle(Vector3.UnitX, _pitch);
        }

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
            var yawRotation = Quaternion.CreateFromAxisAngle(Vector3.UnitY, _yaw);
            var worldHorizontalMove = Vector3.Transform(localMove, yawRotation);
            var finalMove = new Vector3(worldHorizontalMove.X, worldVerticalMove, worldHorizontalMove.Z);

            if (finalMove.LengthSquared() > 0)
            {
                GameObject.Transform.Position += Vector3.Normalize(finalMove) * Speed * deltaTime;
            }
        }
    }
}