namespace Cherris
{
    using System.Collections.Generic;
    using System.Numerics;

    /// <summary>
    /// Manages the physics simulation for a scene.
    /// </summary>
    public class PhysicsSystem
    {
        public Jitter.World World { get; }
        private readonly Dictionary<Jitter.Dynamics.RigidBody, RigidBody> _bodyMap = new();

        public PhysicsSystem()
        {
            var collisionSystem = new Jitter.Collision.CollisionSystemSAP();
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
            if (jitterBody != null && _bodyMap.ContainsKey(jitterBody))
            {
                _bodyMap.Remove(jitterBody);
                World.RemoveBody(jitterBody);
            }
        }

        public void Update(float deltaTime)
        {
            // The Jitter 1 Step method takes a timestep and a boolean for multithreading.
            World.Step(deltaTime, true);

            // Sync transforms from physics back to game objects for non-static bodies
            foreach (var (jitterBody, bodyComponent) in _bodyMap)
            {
                if (jitterBody.IsStatic) continue;

                bodyComponent.GameObject.Transform.Position = jitterBody.Position.ToNumerics();
                bodyComponent.GameObject.Transform.Rotation = jitterBody.Orientation.ToNumerics();
            }
        }
    }
}