using BepuPhysics;
using BepuPhysics.Collidables;
using Cherris.Core;
using Cherris.Utils;
using System.Numerics;

namespace Cherris.Components;

public enum ColliderType
{
    Box,
    Sphere,
    Capsule
}

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
    public BodyHandle? BepuBodyHandle { get; internal set; }

    [HideInInspector]
    public StaticHandle? BepuStaticHandle { get; internal set; }

    private PhysicsSystem? _physicsSystem;
    private Vector3 _lastScale;

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
        CreateAndRegisterBepuBody();
    }

    public override void Update(float deltaTime)
    {
        if (GameObject is not null && GameObject.Transform.Scale != _lastScale)
        {
            RecreatePhysicsBody();
        }
    }

    private void RecreatePhysicsBody()
    {
        if (_physicsSystem is null)
        {
            return;
        }

        RemovePhysicsBody();
        CreateAndRegisterBepuBody();
    }

    private void RemovePhysicsBody()
    {
        if (BepuBodyHandle.HasValue && _physicsSystem?.Simulation is not null)
        {
            _physicsSystem.Simulation.Bodies.Remove(BepuBodyHandle.Value);
            _physicsSystem.UnregisterBody(this);
            BepuBodyHandle = null;
        }

        if (BepuStaticHandle.HasValue && _physicsSystem?.Simulation is not null)
        {
            _physicsSystem.Simulation.Statics.Remove(BepuStaticHandle.Value);
            _physicsSystem.UnregisterBody(this);
            BepuStaticHandle = null;
        }
    }

    private void CreateAndRegisterBepuBody()
    {
        var meshRenderer = GameObject.GetComponent<MeshRenderer>();

        if (meshRenderer is null)
        {
            Console.WriteLine($"[RigidBody] Warning: No MeshRenderer found on '{GameObject.Name}'. Cannot create physics shape.");
            return;
        }

        BoundingBox aabb = meshRenderer.Mesh.AABB;
        _lastScale = GameObject.Transform.Scale;
        Vector3 size = (aabb.Max - aabb.Min) * _lastScale;

        if (_physicsSystem?.Simulation is null)
        {
            return;
        }

        var position = GameObject.Transform.Position;
        var orientation = GameObject.Transform.Rotation;
        var pose = new RigidPose(position, orientation);

        if (IsStatic)
        {
            CreateStaticBody(size, pose);
        }
        else
        {
            CreateDynamicBody(size, pose);
        }

        _physicsSystem.RegisterBody(this);
    }

    private void CreateStaticBody(Vector3 size, RigidPose pose)
    {
        if (_physicsSystem?.Simulation is null)
        {
            return;
        }

        TypedIndex shapeIndex;
        switch (Shape)
        {
            case ColliderType.Box:
                var box = new BepuPhysics.Collidables.Box(size.X, size.Y, size.Z);
                shapeIndex = _physicsSystem.Simulation.Shapes.Add(box);
                break;

            case ColliderType.Sphere:
                float sphereRadius = (size.X + size.Y + size.Z) / 6.0f;
                var sphere = new Sphere(sphereRadius);
                shapeIndex = _physicsSystem.Simulation.Shapes.Add(sphere);
                break;

            case ColliderType.Capsule:
                float capsuleRadius = Math.Max(size.X, size.Z) / 2.0f;
                float capsuleLength = Math.Max(0, size.Y - (2 * capsuleRadius));
                var capsule = new Capsule(capsuleRadius, capsuleLength);
                shapeIndex = _physicsSystem.Simulation.Shapes.Add(capsule);
                break;

            default:
                var defaultBox = new BepuPhysics.Collidables.Box(size.X, size.Y, size.Z);
                shapeIndex = _physicsSystem.Simulation.Shapes.Add(defaultBox);
                break;
        }

        // Create static description - use the constructor that takes pose and shape index directly
        // Based on the error, StaticDescription constructor takes (RigidPose, TypedIndex)
        var staticDescription = new StaticDescription(pose, shapeIndex);

        BepuStaticHandle = _physicsSystem.Simulation.Statics.Add(staticDescription);
        BepuBodyHandle = null;
    }

    private void CreateDynamicBody(Vector3 size, RigidPose pose)
    {
        if (_physicsSystem?.Simulation is null)
        {
            return;
        }

        BodyInertia inertia;
        TypedIndex shapeIndex;

        switch (Shape)
        {
            case ColliderType.Box:
                var box = new BepuPhysics.Collidables.Box(size.X, size.Y, size.Z);
                inertia = box.ComputeInertia(Mass);
                shapeIndex = _physicsSystem.Simulation.Shapes.Add(box);
                break;

            case ColliderType.Sphere:
                float sphereRadius = (size.X + size.Y + size.Z) / 6.0f;
                var sphere = new Sphere(sphereRadius);
                inertia = sphere.ComputeInertia(Mass);
                shapeIndex = _physicsSystem.Simulation.Shapes.Add(sphere);
                break;

            case ColliderType.Capsule:
                float capsuleRadius = Math.Max(size.X, size.Z) / 2.0f;
                float capsuleLength = Math.Max(0, size.Y - (2 * capsuleRadius));
                var capsule = new Capsule(capsuleRadius, capsuleLength);
                inertia = capsule.ComputeInertia(Mass);
                shapeIndex = _physicsSystem.Simulation.Shapes.Add(capsule);
                break;

            default:
                var defaultBox = new BepuPhysics.Collidables.Box(size.X, size.Y, size.Z);
                inertia = defaultBox.ComputeInertia(Mass);
                shapeIndex = _physicsSystem.Simulation.Shapes.Add(defaultBox);
                break;
        }

        var collidable = new CollidableDescription(shapeIndex, 0.1f);
        var activity = new BodyActivityDescription(0.01f);

        var bodyDescription = BodyDescription.CreateDynamic(pose, inertia, collidable, activity);

        BepuBodyHandle = _physicsSystem.Simulation.Bodies.Add(bodyDescription);
        BepuStaticHandle = null;
    }

    public void ApplyForce(Vector3 force)
    {
        if (!BepuBodyHandle.HasValue || _physicsSystem?.Simulation is null || IsStatic)
        {
            return;
        }

        var bodyReference = _physicsSystem.Simulation.Bodies.GetBodyReference(BepuBodyHandle.Value);
        if (bodyReference.Exists)
        {
            bodyReference.Velocity.Linear += force / Mass;
        }
    }

    public void ApplyImpulse(Vector3 impulse)
    {
        if (!BepuBodyHandle.HasValue || _physicsSystem?.Simulation is null || IsStatic)
        {
            return;
        }

        var bodyReference = _physicsSystem.Simulation.Bodies.GetBodyReference(BepuBodyHandle.Value);
        if (bodyReference.Exists)
        {
            bodyReference.Velocity.Linear += impulse / Mass;
        }
    }

    public void SetVelocity(Vector3 velocity)
    {
        if (!BepuBodyHandle.HasValue || _physicsSystem?.Simulation is null || IsStatic)
        {
            return;
        }

        var bodyReference = _physicsSystem.Simulation.Bodies.GetBodyReference(BepuBodyHandle.Value);
        if (bodyReference.Exists)
        {
            bodyReference.Velocity.Linear = velocity;
        }
    }

    public Vector3 GetVelocity()
    {
        if (!BepuBodyHandle.HasValue || _physicsSystem?.Simulation is null || IsStatic)
        {
            return Vector3.Zero;
        }

        var bodyReference = _physicsSystem.Simulation.Bodies.GetBodyReference(BepuBodyHandle.Value);
        if (bodyReference.Exists)
        {
            return bodyReference.Velocity.Linear;
        }

        return Vector3.Zero;
    }

    public void Teleport(Vector3 position, Quaternion? rotation = null)
    {
        if (BepuBodyHandle.HasValue && _physicsSystem?.Simulation is not null && !IsStatic)
        {
            var bodyReference = _physicsSystem.Simulation.Bodies.GetBodyReference(BepuBodyHandle.Value);
            if (bodyReference.Exists)
            {
                bodyReference.Pose.Position = position;
                if (rotation.HasValue)
                {
                    bodyReference.Pose.Orientation = rotation.Value;
                }
            }
        }

        GameObject.Transform.Position = position;
        if (rotation.HasValue)
        {
            GameObject.Transform.Rotation = rotation.Value;
        }
    }

    public void OnDestroy()
    {
        RemovePhysicsBody();
    }
}