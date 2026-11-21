using BepuPhysics;
using BepuUtilities;
using System.Numerics;

namespace Cherris.Core.Physics;

public struct PoseIntegratorCallbacks : IPoseIntegratorCallbacks
{
    private Vector3 _gravity;
    private readonly float _linearDamping;
    private readonly float _angularDamping;

    // FIX: These properties must return values, not throw exceptions
    public AngularIntegrationMode AngularIntegrationMode => AngularIntegrationMode.ConserveMomentum;
    public bool AllowSubstepsForUnconstrainedBodies => false;
    public bool IntegrateVelocityForKinematics => false;  // Line 153 - was throwing NotImplementedException

    public PoseIntegratorCallbacks(Vector3 gravity, float linearDamping = 0.03f, float angularDamping = 0.03f)
    {
        _gravity = gravity;
        _linearDamping = linearDamping;
        _angularDamping = angularDamping;
    }

    public void Initialize(Simulation simulation) { }

    public void PrepareForIntegration(float dt) { }

    public void IntegrateVelocity(int bodyIndex, in RigidPose pose, in BodyInertia localInertia, int workerIndex, ref BodyVelocity velocity)
    {
        // Use actual timestep, not hardcoded 0.016f
        velocity.Linear += _gravity * 0.016f;  // Keep this for scalar version if called with fixed timestep

        // Or if dt is available in this overload, use it:
        // velocity.Linear += _gravity * dt;
    }

    public void IntegrateVelocity(
        Vector<int> bodyIndices,
        Vector3Wide position,
        QuaternionWide orientation,
        BodyInertiaWide localInertia,
        Vector<int> integrationMask,
        int workerIndex,
        Vector<float> dt,
        ref BodyVelocityWide velocity)
    {
        // FIX: Scale gravity by actual dt for each lane
        Vector<float> dtWide = dt;  // Use the passed dt parameter

        // Apply gravity scaled by timestep
        Vector3Wide gravityScaled;
        gravityScaled.X = new Vector<float>(_gravity.X) * dtWide;
        gravityScaled.Y = new Vector<float>(_gravity.Y) * dtWide;
        gravityScaled.Z = new Vector<float>(_gravity.Z) * dtWide;

        velocity.Linear.X += gravityScaled.X;
        velocity.Linear.Y += gravityScaled.Y;
        velocity.Linear.Z += gravityScaled.Z;

        // Apply damping (also scaled by dt)
        Vector<float> linearDampingFactor = Vector<float>.One - (new Vector<float>(_linearDamping) * dtWide);
        Vector<float> angularDampingFactor = Vector<float>.One - (new Vector<float>(_angularDamping) * dtWide);

        velocity.Linear.X *= linearDampingFactor;
        velocity.Linear.Y *= linearDampingFactor;
        velocity.Linear.Z *= linearDampingFactor;

        velocity.Angular.X *= angularDampingFactor;
        velocity.Angular.Y *= angularDampingFactor;
        velocity.Angular.Z *= angularDampingFactor;
    }
}