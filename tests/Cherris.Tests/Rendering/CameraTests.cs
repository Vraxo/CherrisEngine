using AwesomeAssertions;
using Cherris.Components;
using System.Numerics;
using Xunit;

namespace Cherris.Tests;

/// <summary>
/// Covers the values a camera is drawn through. They are the one part of a scene a renderer cannot survive being
/// wrong about: a negative near plane makes OpenTK's <c>CreatePerspectiveFieldOfView</c> throw, and the render
/// loop stops with it. Every edit path — the inspector, a drag, a reset, a script, a scene file — writes through
/// these setters, so the tests are about the setters holding the line.
/// </summary>
public class CameraTests
{
    [Fact]
    public void ANewCamera_HasTheUsualValues()
    {
        Camera camera = new();

        camera.FieldOfView.Should().Be(60f);
        camera.NearClipPlane.Should().Be(0.1f);
        camera.FarClipPlane.Should().Be(100f);
    }

    [Fact]
    public void ANegativeNearPlane_IsHeldAtTheMinimum()
    {
        Camera camera = new();

        // What a drag two pixels to the left of 0.02 used to write, and what killed the render thread.
        camera.NearClipPlane = -0.02f;

        camera.NearClipPlane.Should().Be(Camera.MinimumNearClipPlane);
        Projection(camera).Should().OnlyContain(value => float.IsFinite(value), "a projection can be built");
    }

    [Fact]
    public void AZeroNearPlane_IsHeldAtTheMinimum()
    {
        Camera camera = new();

        camera.NearClipPlane = 0f;

        camera.NearClipPlane.Should().Be(Camera.MinimumNearClipPlane);
    }

    [Fact]
    public void ANearPlanePushedPastTheFarOne_TakesTheFarPlaneWithIt()
    {
        Camera camera = new() { FarClipPlane = 10f };

        camera.NearClipPlane = 50f;

        camera.NearClipPlane.Should().Be(50f);
        camera.FarClipPlane.Should().BeGreaterThan(camera.NearClipPlane, "a frustum with no depth is not one");
        camera.FarClipPlane.Should().Be(100f, "the near plane is what was asked for, so the far one moves");
    }

    [Fact]
    public void AFarPlaneInFrontOfTheNearOne_IsRaisedAboveIt()
    {
        Camera camera = new() { NearClipPlane = 2f };

        camera.FarClipPlane = 0.5f;

        camera.FarClipPlane.Should().Be(4f, "the far plane is kept twice as deep as the near one");
    }

    [Fact]
    public void AValueThatIsNotANumber_IsIgnored()
    {
        Camera camera = new() { NearClipPlane = 0.5f, FarClipPlane = 80f, FieldOfView = 75f };

        camera.NearClipPlane = float.NaN;
        camera.FarClipPlane = float.PositiveInfinity;
        camera.FieldOfView = float.NaN;

        camera.NearClipPlane.Should().Be(0.5f, "NaN is not a distance, and clamping it would store NaN");
        camera.FarClipPlane.Should().Be(80f);
        camera.FieldOfView.Should().Be(75f);
    }

    [Fact]
    public void TheFieldOfView_StaysWhereAProjectionAcceptsIt()
    {
        Camera camera = new();

        camera.FieldOfView = 0f;
        camera.FieldOfView.Should().Be(Camera.MinimumFieldOfView);

        camera.FieldOfView = 400f;
        camera.FieldOfView.Should().Be(Camera.MaximumFieldOfView);

        camera.FieldOfView = -30f;
        camera.FieldOfView.Should().Be(Camera.MinimumFieldOfView);

        camera.FieldOfView = 90f;
        camera.FieldOfViewRadians.Should().BeApproximately(MathF.PI / 2f, 0.0001f);
    }

    [Fact]
    public void TheProjection_SurvivesANonsenseAspect()
    {
        Camera camera = new();

        Projection(camera, aspect: 0f).Should().OnlyContain(value => float.IsFinite(value));
        Projection(camera, aspect: float.NaN).Should().OnlyContain(value => float.IsFinite(value));
        Projection(camera, aspect: -2f).Should().OnlyContain(value => float.IsFinite(value));
    }

    [Fact]
    public void TheProjection_PutsTheClipPlanesAtTheEndsOfTheDepthRange()
    {
        Camera camera = new();

        Matrix4x4 projection = camera.GetProjectionMatrix(16f / 9f);

        Depth(projection, -camera.NearClipPlane).Should().BeApproximately(0f, 0.001f,
            "the near plane is the front of the clip range");
        Depth(projection, -camera.FarClipPlane).Should().BeApproximately(1f, 0.001f,
            "and the far plane the back of it");
    }

    /// <summary>The depth a point straight ahead of the camera lands on, after the perspective divide.</summary>
    private static float Depth(Matrix4x4 projection, float z)
    {
        Vector4 clip = Vector4.Transform(new Vector4(0f, 0f, z, 1f), projection);

        return clip.Z / clip.W;
    }

    private static IReadOnlyList<float> Projection(Camera camera, float aspect = 16f / 9f)
    {
        Matrix4x4 projection = camera.GetProjectionMatrix(aspect);

        return
        [
            projection.M11, projection.M12, projection.M13, projection.M14,
            projection.M21, projection.M22, projection.M23, projection.M24,
            projection.M31, projection.M32, projection.M33, projection.M34,
            projection.M41, projection.M42, projection.M43, projection.M44
        ];
    }
}
