using System.Numerics;

namespace Cherris.Components;

public class RigidBody : Script
{
    public ColliderType Shape
    {
        get;

        set
        {
            if (field == value)
            {
                return;
            }

            field = value;
            RecreatePhysicsBody();
        }
    } = ColliderType.Box;

    public float Mass { get; set; } = 1.0f;
    public bool IsStatic { get; set; } = false;
    public float Friction { get; set; } = 0.5f;
    public float Bounciness { get; set; } = 0.5f;

    [HideInInspector]
    public Jitter.Dynamics.RigidBody? JitterBody { get; private set; }

    private PhysicsSystem? _physicsSystem;
    private Vector3 _lastScale;

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

        CreateAndRegisterJitterBody();
    }

    public override void Update(float deltaTime)
    {
        // If the object's scale has changed, recreate the physics body to match.
        if (GameObject is null || GameObject.Transform.Scale == _lastScale)
        {
            return;
        }

        RecreatePhysicsBody();
    }

    private void RecreatePhysicsBody()
    {
        if (JitterBody is null || _physicsSystem is null)
        {
            return;
        }

        _physicsSystem.RemoveBody(JitterBody);
        JitterBody = null;
        CreateAndRegisterJitterBody();
    }

    private void CreateAndRegisterJitterBody()
    {
        var meshRenderer = GameObject.GetComponent<MeshRenderer>();

        if (meshRenderer is null)
        {
            Console.WriteLine($"" +
                $"[RigidBody] Warning: No MeshRenderer found on '{GameObject.Name}'." +
                $"Cannot create physics shape.");

            return;
        }

        BoundingBox aabb = meshRenderer.Mesh.AABB;
        _lastScale = GameObject.Transform.Scale; // Store the scale used for creation
        Vector3 size = (aabb.Max - aabb.Min) * _lastScale;

        Jitter.Collision.Shapes.Shape shape;

        shape = GetShape(size);

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

    private Jitter.Collision.Shapes.Shape GetShape(Vector3 size)
    {
        return Shape switch
        {
            ColliderType.Box => new Jitter.Collision.Shapes.BoxShape(size.ToJitter()),

            ColliderType.Sphere => new Jitter.Collision.Shapes.SphereShape(
                (size.X + size.Y + size.Z) / 6.0f // Average radius
            ),

            ColliderType.Capsule => new Jitter.Collision.Shapes.CapsuleShape(
                float.Max(0, size.Y - (2 * float.Max(size.X, size.Z) / 2.0f)), // Capsule length
                float.Max(size.X, size.Z) / 2.0f // Capsule radius
            ),

            _ => LogAndReturnDefault(size)
        };
    }

    private Jitter.Collision.Shapes.Shape LogAndReturnDefault(Vector3 size)
    {
        Console.WriteLine(
            $"[RigidBody] Warning: Unsupported collider type '{Shape}' on '{GameObject.Name}'." +
            $"Defaulting to Box.");

        return new Jitter.Collision.Shapes.BoxShape(size.ToJitter());
    }
}