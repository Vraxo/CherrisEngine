using Cherris;
using System.Numerics;

namespace Cherris
{
    /// <summary>
    /// Adds physics simulation properties to a GameObject.
    /// This component requires a MeshRenderer to determine the collision shape.
    /// </summary>
    public class RigidBody : Script
    {
        public float Mass { get; set; } = 1.0f;
        public bool IsStatic { get; set; } = false;
        public float Friction { get; set; } = 0.5f;
        public float Bounciness { get; set; } = 0.5f;

        [HideInInspector]
        public Jitter.Dynamics.RigidBody JitterBody { get; private set; }

        private PhysicsSystem _physicsSystem;

        // Called by the Scene to provide the PhysicsSystem instance.
        internal void Initialize(PhysicsSystem physicsSystem)
        {
            _physicsSystem = physicsSystem;
        }

        public override void Start()
        {
            if (_physicsSystem is null)
            {
                // This can happen if the component is added at runtime after the scene has already started.
                // A more robust solution would be needed for that case.
                return;
            }

            var meshRenderer = GameObject.GetComponent<MeshRenderer>();
            if (meshRenderer == null)
            {
                System.Console.WriteLine($"[RigidBody] Warning: No MeshRenderer found on '{GameObject.Name}'. Cannot create physics shape.");
                return;
            }

            var aabb = meshRenderer.Mesh.AABB;
            var size = (aabb.Max - aabb.Min) * GameObject.Transform.Scale;
            var shape = new Jitter.Collision.Shapes.BoxShape(size.ToJitter());

            JitterBody = new Jitter.Dynamics.RigidBody(shape)
            {
                Position = GameObject.Transform.Position.ToJitter(),
                Orientation = GameObject.Transform.Rotation.ToJitter()
            };

            if (IsStatic)
            {
                JitterBody.IsStatic = true;
            }
            else
            {
                JitterBody.Mass = Mass;
            }

            JitterBody.Material.StaticFriction = Friction;
            JitterBody.Material.KineticFriction = Friction;
            JitterBody.Material.Restitution = Bounciness;

            _physicsSystem.AddBody(this, JitterBody);
        }
    }
}