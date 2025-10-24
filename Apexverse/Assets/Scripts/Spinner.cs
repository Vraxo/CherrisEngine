using System.Numerics;
using Cherris;

namespace Apexverse;

public class Spinner : Script
{
    private float _totalTime;

    public override void Update(float deltaTime)
    {
        _totalTime += deltaTime;
        GameObject.Transform.Rotation = Quaternion.CreateFromAxisAngle(Vector3.UnitY, _totalTime) *
                                           Quaternion.CreateFromAxisAngle(Vector3.UnitX, _totalTime * 0.7f);
    }
}
