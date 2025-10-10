using Cherris;
using System.Numerics;
using Veldrid;

namespace Apexverse
{
    public class PlayerController : Script
    {
        public float Speed { get; set; } = 3.0f;

        public override void Update(float deltaTime)
        {
            var direction = Vector3.Zero;

            if (Input.IsKeyDown(Key.W))
            {
                direction -= Vector3.UnitZ;
            }
            if (Input.IsKeyDown(Key.S))
            {
                direction += Vector3.UnitZ;
            }
            if (Input.IsKeyDown(Key.A))
            {
                direction -= Vector3.UnitX;
            }
            if (Input.IsKeyDown(Key.D))
            {
                direction += Vector3.UnitX;
            }

            if (direction != Vector3.Zero)
            {
                direction = Vector3.Normalize(direction);
                // The direction is local. Transform it to world space by the object's rotation.
                var worldDirection = Vector3.Transform(direction, GameObject.Transform.Rotation);
                GameObject.Transform.Position += worldDirection * Speed * deltaTime;
            }
        }
    }
}