using System.Numerics;
using AwesomeAssertions;
using Xunit;

namespace Overcore.Tests;

public class EngineMathTests
{
    [Fact]
    public void IdentityQuaternionHasZeroEulerAngles()
    {
        EngineMath.ToEulerAngles(Quaternion.Identity).Should().Be(Vector3.Zero);
    }

    [Theory]
    [InlineData(0.5f)]
    [InlineData(-0.5f)]
    public void ConvertsRotationAroundXAxis(float angle)
    {
        var result = EngineMath.ToEulerAngles(Quaternion.CreateFromAxisAngle(Vector3.UnitX, angle));

        result.X.Should().BeApproximately(angle, 0.00001f);
        result.Y.Should().BeApproximately(0, 0.00001f);
        result.Z.Should().BeApproximately(0, 0.00001f);
    }
}
