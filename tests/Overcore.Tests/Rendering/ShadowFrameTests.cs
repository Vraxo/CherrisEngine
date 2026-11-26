using AwesomeAssertions;
using Overcore.Components;
using Overcore.Core;
using Overcore.Rendering;
using Overcore.Rendering.Shadows;
using Xunit;

namespace Overcore.Tests.Rendering;

/// <summary>
/// What a frame gathers for the depth pass, and — more importantly — the cases in which it gathers nothing. A frame
/// that returns a half-built cascade list is a frame that draws into a map the receiver will sample, so every reason to
/// skip is <see cref="ShadowFrame.None"/> rather than a partially filled instance.
/// </summary>
public class ShadowFrameTests
{
    private static Camera Camera()
    {
        GameObject gameObject = new("camera");
        Camera camera = new();
        gameObject.AddComponent(camera);

        return camera;
    }

    private static GameObject CascadingLight(ShadowType shadows = ShadowType.Soft)
    {
        GameObject gameObject = new("sun");
        gameObject.AddComponent(new Light { Type = LightType.Directional, Shadows = shadows });

        return gameObject;
    }

    // ---------------------------------------------------------------- when there is no frame

    [Fact]
    public void NoCameraIsNoFrame()
    {
        ShadowFrame frame = ShadowFrame.Build([CascadingLight()], null, 16f / 9f);

        frame.Enabled.Should().BeFalse();
        frame.Cascades.Should().BeEmpty();
    }

    [Fact]
    public void NoCastingLightIsNoFrame()
    {
        // A scene lit by point lights only. It still draws; it just has nothing to project.
        GameObject gameObject = new("lamp");
        gameObject.AddComponent(new Light { Type = LightType.Point });

        ShadowFrame frame = ShadowFrame.Build([gameObject], Camera(), 16f / 9f);

        frame.Enabled.Should().BeFalse();
    }

    [Fact]
    public void ADirectionalLightThatDoesNotCastIsNoFrame()
    {
        ShadowFrame frame = ShadowFrame.Build(
            [CascadingLight(ShadowType.None)],
            Camera(),
            16f / 9f);

        frame.Enabled.Should().BeFalse();
    }

    [Fact]
    public void ADisabledLightIsNoFrame()
    {
        GameObject gameObject = CascadingLight();
        gameObject.GetComponent<Light>()!.Enabled = false;

        ShadowFrame.Build([gameObject], Camera(), 16f / 9f).Enabled.Should().BeFalse();
    }

    [Fact]
    public void ALightOnAnInactiveObjectIsNoFrame()
    {
        GameObject gameObject = CascadingLight();
        gameObject.SetActive(false);

        ShadowFrame.Build([gameObject], Camera(), 16f / 9f).Enabled.Should().BeFalse();
    }

    [Fact]
    public void AnEmptySceneIsNoFrame()
    {
        ShadowFrame.Build([], Camera(), 16f / 9f).Enabled.Should().BeFalse();
        ShadowFrame.Build(null, Camera(), 16f / 9f).Enabled.Should().BeFalse();
    }

    // ---------------------------------------------------------------- when there is

    [Fact]
    public void ACastingDirectionalLightAndACameraGiveFourCascades()
    {
        ShadowFrame frame = ShadowFrame.Build([CascadingLight()], Camera(), 16f / 9f);

        frame.Enabled.Should().BeTrue();
        frame.Cascades.Should().HaveCount(CascadeSplits.DefaultCascades);
        frame.Ranges.Should().HaveCount(CascadeSplits.DefaultCascades);

        // The depths the document promises at the defaults: 10, 30, 70 and 150 metres.
        frame.Ranges[^1].Far.Should().Be(CascadeSplits.DefaultDistance);
    }

    [Fact]
    public void EveryCascadeCarriesEightWorldCorners()
    {
        ShadowFrame frame = ShadowFrame.Build([CascadingLight()], Camera(), 16f / 9f);

        frame.SliceCorners.Should().HaveCount(frame.Cascades.Count * 8);
    }

    [Fact]
    public void TheSliceCornersOfTheFirstCascadeSitInFrontOfTheCamera()
    {
        // A camera at the origin looking down -Z with no rotation. The first cascade covers the near plane to ten
        // metres, so its corners are all ahead of the eye and none is behind it. A sign error in the camera matrix
        // puts every corner behind, which culls every caster in the scene and reads as "shadows are broken".
        GameObject gameObject = new("camera");
        Camera camera = new();
        gameObject.AddComponent(camera);

        ShadowFrame frame = ShadowFrame.Build([CascadingLight()], camera, 16f / 9f);

        frame.Enabled.Should().BeTrue();

        for (int corner = 0; corner < 8; corner++)
        {
            // Camera looks down -Z, so "in front" is a negative Z.
            frame.SliceCorners[corner].Z.Should().BeLessThan(0f);
        }
    }

    [Fact]
    public void TheCascadesGetFartherFromTheCameraAsTheyGoOut()
    {
        ShadowFrame frame = ShadowFrame.Build([CascadingLight()], Camera(), 16f / 9f);

        float previous = 0f;

        foreach (CascadeRange range in frame.Ranges)
        {
            range.Near.Should().BeGreaterThanOrEqualTo(previous);
            range.Far.Should().BeGreaterThan(range.Near);
            previous = range.Far;
        }
    }

    [Fact]
    public void TheFirstCastingDirectionalLightIsTheOneUsed()
    {
        // The same light OpenGLSceneRenderer shades from, chosen with FirstOrDefault. A scene with two directional
        // lights shades and shadows from the first; the second's ShadowType is inert rather than half-honoured.
        GameObject first = CascadingLight();
        GameObject second = CascadingLight(ShadowType.None);

        ShadowFrame frame = ShadowFrame.Build([first, second], Camera(), 16f / 9f);

        frame.Enabled.Should().BeTrue();
        frame.Light.Should().Be(first.GetComponent<Light>());
    }

    [Fact]
    public void TheLightDirectionOpposesTheDirectionToIt()
    {
        // uDirLight.direction in the shading programs is the direction *to* the light, and the cascades are fitted
        // looking back along it. Getting this backwards lights and shadows the scene from opposite ends.
        GameObject gameObject = CascadingLight();
        gameObject.Transform.Rotation = System.Numerics.Quaternion.CreateFromAxisAngle(
            System.Numerics.Vector3.UnitX,
            MathF.PI / 4f);

        ShadowFrame frame = ShadowFrame.Build([gameObject], Camera(), 16f / 9f);

        // LightDirection is the direction the light travels, so it is the negation of the direction to it — the axis
        // the fit itself looks along.
        System.Numerics.Vector3 toLight = LightSpaceFit.DirectionToLight(gameObject.Transform.Rotation);

        frame.LightDirection.X.Should().BeApproximately(-toLight.X, 0.001f);
        frame.LightDirection.Y.Should().BeApproximately(-toLight.Y, 0.001f);
        frame.LightDirection.Z.Should().BeApproximately(-toLight.Z, 0.001f);
    }

    // ---------------------------------------------------------------- resolution

    [Fact]
    public void AnExplicitResolutionOverridesTheLightsSetting()
    {
        GameObject gameObject = CascadingLight();
        gameObject.GetComponent<Light>()!.ShadowResolution = ShadowResolution.Low;

        ShadowFrame.Build([gameObject], Camera(), 16f / 9f, 3072).Resolution.Should().Be(3072);
    }

    [Fact]
    public void WithNoExplicitResolutionTheLightsSettingDecides()
    {
        GameObject gameObject = CascadingLight();
        gameObject.GetComponent<Light>()!.ShadowResolution = ShadowResolution.Low;

        // The parameter defaults to zero, meaning "ask the light". A caller that passed nothing used to get 2048
        // regardless of what the light asked for, which made the setting inert without failing anything.
        ShadowFrame.Build([gameObject], Camera(), 16f / 9f).Resolution.Should().Be(512);
    }

    [Fact]
    public void TheResolutionFollowsTheCascadesTexelSize()
    {
        GameObject gameObject = CascadingLight();

        ShadowFrame small = ShadowFrame.Build([gameObject], Camera(), 16f / 9f, 1024);
        ShadowFrame large = ShadowFrame.Build([gameObject], Camera(), 16f / 9f, 4096);

        // Twice the texels a side is half the world per texel, which is what makes the normal offset scale with the
        // resolution instead of detaching a shadow at exactly one of them.
        //
        // A ratio with a relative tolerance, not `small / 2 ≈ large`: the texel size is the box's extent over the map
        // side, and the extent itself grows with the texel size (the fit pads the box by a texel on every axis so a
        // caster on the boundary still lands inside). So the ratio is 2 and the difference is not exactly zero, which
        // is a property of the fit rather than a rounding artefact to widen a tolerance around.
        (small.Cascades[0].TexelSize / large.Cascades[0].TexelSize).Should().BeApproximately(2f, 0.01f);
    }

    // ---------------------------------------------------------------- the singleton

    [Fact]
    public void NoneIsADisabledFrameRatherThanNull()
    {
        // A backend without a depth pass — Veldrid, today — is handed this and draws exactly what it drew before.
        // Returning null would put a null check in both backends and a null dereference in whichever one forgot it.
        ShadowFrame.None.Enabled.Should().BeFalse();
        ShadowFrame.None.Cascades.Should().BeEmpty();
        ShadowFrame.None.Light.Should().BeNull();
        ShadowFrame.None.Resolution.Should().Be(Shadows.DefaultResolution);
    }
}
