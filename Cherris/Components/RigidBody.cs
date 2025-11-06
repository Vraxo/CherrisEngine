using System.Numerics;

namespace Cherris.Components;

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
            return;
        }

        var meshRenderer = GameObject.GetComponent<MeshRenderer>();

        if (meshRenderer == null)
        {
            Console.WriteLine($"[RigidBody] Warning: No MeshRenderer found on '{GameObject.Name}'. Cannot create physics shape.");
            return;
        }

        BoundingBox aabb = meshRenderer.Mesh.AABB;
        Vector3 size = (aabb.Max - aabb.Min) * GameObject.Transform.Scale;
        Jitter.Collision.Shapes.BoxShape shape = new(size.ToJitter());

        JitterBody = new(shape)
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