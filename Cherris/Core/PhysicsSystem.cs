namespace Cherris;

using Jitter.Collision;
using System.Collections.Generic;
using System.Numerics;

public class PhysicsSystem
{
    public Jitter.World World { get; }
    private readonly Dictionary<Jitter.Dynamics.RigidBody, RigidBody> _bodyMap = new();

    private float _accumulator = 0.0f;
    private const float FixedTimeStep = 1.0f / 60.0f;
    private const float MaxDeltaTime = 0.25f; // Prevents "spiral of death" from very large delta times

    public PhysicsSystem()
    {
        CollisionSystemSAP collisionSystem = new();

        World = new Jitter.World(collisionSystem)
        {
            Gravity = new Vector3(0, -9.81f, 0).ToJitter()
        };
    }

    public void AddBody(RigidBody bodyComponent, Jitter.Dynamics.RigidBody jitterBody)
    {
        _bodyMap.Add(jitterBody, bodyComponent);
        World.AddBody(jitterBody);
    }

    public void RemoveBody(Jitter.Dynamics.RigidBody jitterBody)
    {
        if (jitterBody == null || !_bodyMap.ContainsKey(jitterBody))
        {
            return;
        }

        _bodyMap.Remove(jitterBody);
        World.RemoveBody(jitterBody);
    }

    public void Update(float deltaTime)
    {
        // Clamp the delta time to prevent huge simulation steps after a freeze
        float effectiveDeltaTime = Math.Min(deltaTime, MaxDeltaTime);
        _accumulator += effectiveDeltaTime;

        // Step the simulation in fixed increments to maintain stability
        while (_accumulator >= FixedTimeStep)
        {
            World.Step(FixedTimeStep, true);
            _accumulator -= FixedTimeStep;
        }

        // Sync transforms from physics back to game objects for non-static bodies
        foreach ((Jitter.Dynamics.RigidBody jitterBody, RigidBody bodyComponent) in _bodyMap)
        {
            if (jitterBody.IsStatic)
            {
                continue;
            }

            bodyComponent.GameObject.Transform.Position = jitterBody.Position.ToNumerics();
            bodyComponent.GameObject.Transform.Rotation = jitterBody.Orientation.ToNumerics();
        }
    }
}