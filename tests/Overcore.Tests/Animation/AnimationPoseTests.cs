using AwesomeAssertions;
using Overcore.Animation;
using Overcore.Components;
using System.Numerics;
using Xunit;

namespace Overcore.Tests.AnimationTests;

/// <summary>
/// Sampling a clip into a pose, and blending poses together. Everything here is arithmetic over arrays, so the
/// assertions are values rather than tolerances wherever the maths is exact.
/// </summary>
public class AnimationPoseTests
{
    [Fact]
    public void Sample_AtAKeysExactTime_ReturnsThatKeysValue()
    {
        AnimationClip clip = Translate((0f, 0f), (1f, 10f));

        AnimationPose pose = new();
        clip.SampleAt(1f, pose);

        pose.GetPosition("Bone").Y.Should().Be(10f);
    }

    [Fact]
    public void Sample_AtTheStart_ReturnsTheFirstKey()
    {
        AnimationClip clip = Translate((0f, 2f), (1f, 10f));

        AnimationPose pose = new();
        clip.SampleAt(0f, pose);

        pose.GetPosition("Bone").Y.Should().Be(2f);
    }

    [Fact]
    public void Sample_MidSegment_MatchesAHandComputedLerp()
    {
        // Keys at 0 and 1 holding 0 and 10: a quarter of the way is 2.5, and three quarters is 7.5.
        AnimationClip clip = Translate((0f, 0f), (1f, 10f));

        AnimationPose pose = new();

        clip.SampleAt(0.25f, pose);
        pose.GetPosition("Bone").Y.Should().BeApproximately(2.5f, 1e-5f);

        clip.SampleAt(0.75f, pose);
        pose.GetPosition("Bone").Y.Should().BeApproximately(7.5f, 1e-5f);
    }

    [Fact]
    public void Sample_OfASteppedCurve_HoldsTheEarlierKeyUntilTheNextOne()
    {
        // glTF's STEP means "does not move between keys". Sampling it linearly would smooth away exactly the jump the
        // file asked for — the difference between a blinking light and a dimming one.
        AnimationClip clip = Translate((0f, 0f), (1f, 10f));
        clip.Curves[0].Interpolation = AnimationInterpolation.Constant;

        AnimationPose pose = new();

        clip.SampleAt(0.99f, pose);
        pose.GetPosition("Bone").Y.Should().Be(0f);

        clip.SampleAt(1f, pose);
        pose.GetPosition("Bone").Y.Should().Be(10f);
    }

    [Fact]
    public void Sample_OfACubicCurve_FollowsTheTangentsTheFileGave()
    {
        // The importer keeps glTF's tangents; sampling has to keep using them, or a hand-animated arc comes in as
        // straight lines.
        AnimationCurve curve = new("Bone", AnimationProperty.LocalPosition, AnimationAxis.Y)
        {
            Interpolation = AnimationInterpolation.Cubic
        };

        curve.AddKey(new AnimationKey(0f, 0f, 0f, 0f));
        curve.AddKey(new AnimationKey(1f, 1f, 0f, 0f));

        AnimationClip clip = new("Arc");
        clip.AddCurve(curve);

        AnimationPose pose = new();
        clip.SampleAt(0.5f, pose);

        // Smoothstep with zero tangents at both ends: exactly one half rather than a curve that overshoots.
        pose.GetPosition("Bone").Y.Should().BeApproximately(0.5f, 1e-5f);
    }

    [Fact]
    public void Sample_PastTheLastKey_HoldsIt()
    {
        AnimationClip clip = Translate((0f, 0f), (1f, 4f));
        clip.WrapMode = AnimationWrapMode.ClampForever;

        AnimationPose pose = new();
        clip.Sample(99f, pose);

        pose.GetPosition("Bone").Y.Should().Be(4f);
    }

    [Fact]
    public void Sample_LeavesBonesTheClipDoesNotAddress_Untouched()
    {
        // A clip on a hand's worth of bones must not flatten the rest of the body: this is the mask that makes a
        // partial clip usable.
        AnimationClip clip = Translate((0f, 5f), (1f, 5f));

        AnimationPose pose = new();
        pose.Set("Hips", new Vector3(1f, 2f, 3f), Quaternion.Identity, Vector3.One);

        clip.SampleAt(0f, pose);

        pose.GetPosition("Hips").Should().Be(new Vector3(1f, 2f, 3f));
        pose.GetPosition("Bone").Should().Be(new Vector3(0f, 5f, 0f));
    }

    [Fact]
    public void Sample_OfAWholeVector_MatchesSamplingItsThreeAxes()
    {
        // The importer emits whichever form the file used; the sampler must not care which.
        AnimationClip split = new("Split");

        foreach ((AnimationAxis axis, float value) in new[]
                 {
                     (AnimationAxis.X, 1f),
                     (AnimationAxis.Y, 2f),
                     (AnimationAxis.Z, 3f)
                 })
        {
            AnimationCurve curve = new("Bone", AnimationProperty.LocalPosition, axis);
            curve.AddKey(0f, value);
            curve.AddKey(1f, value);

            split.AddCurve(curve);
        }

        AnimationPose pose = new();
        split.SampleAt(0.5f, pose);

        pose.GetPosition("Bone").Should().Be(new Vector3(1f, 2f, 3f));
    }

    [Fact]
    public void Sample_OfAnAxisTheClipDoesNotDrive_KeepsWhatThePoseHeld()
    {
        // Only height is animated. A missing axis is not zero: the object must not slide to x=z=0.
        AnimationClip clip = Translate((0f, 0f), (1f, 2f));

        AnimationPose pose = new();
        pose.Set("Bone", new Vector3(7f, 0f, -3f), Quaternion.Identity, Vector3.One);

        clip.SampleAt(0f, pose);

        pose.GetPosition("Bone").Should().Be(new Vector3(7f, 0f, -3f));
    }

    [Fact]
    public void Sample_OfAQuaternionCurve_ProducesAUnitRotation()
    {
        AnimationClip clip = new("Turn");

        AnimationCurve x = RotationCurve(AnimationAxis.X, 0f, 0.6f);
        AnimationCurve y = RotationCurve(AnimationAxis.Y, 0f, 0.6f);
        AnimationCurve z = RotationCurve(AnimationAxis.Z, 0f, 0.6f);
        AnimationCurve w = RotationCurve(AnimationAxis.W, 1f, 0.6f);

        clip.AddCurve(x);
        clip.AddCurve(y);
        clip.AddCurve(z);
        clip.AddCurve(w);

        AnimationPose pose = new();
        clip.SampleAt(0.5f, pose);

        // Four axes interpolated independently are no longer unit length; the sampler normalizes so the rotation
        // stays a rotation instead of shearing the mesh it drives.
        pose.GetRotation("Bone").Length().Should().BeApproximately(1f, 1e-5f);
    }

    [Fact]
    public void Sample_OfASignFlippedQuaternion_TakesTheShortWayRound()
    {
        // q and -q are the same orientation. Slerping from identity toward a negated key has to spin 10 degrees, not
        // 350: this is the difference the importer's sign-continuity pass exists for.
        Quaternion target = Quaternion.CreateFromAxisAngle(Vector3.UnitY, Degrees(10f));

        AnimationClip clip = new("Turn");

        foreach ((AnimationAxis axis, float value) in new[]
                 {
                     (AnimationAxis.X, -target.X),
                     (AnimationAxis.Y, -target.Y),
                     (AnimationAxis.Z, -target.Z),
                     (AnimationAxis.W, -target.W)
                 })
        {
            AnimationCurve curve = RotationCurve(axis, value, value);
            clip.AddCurve(curve);
        }

        AnimationPose pose = new();
        clip.SampleAt(0.5f, pose);

        // Halfway between the identity and a ten degree turn is a five degree turn, either way round the circle.
        float angle = 2f * MathF.Acos(Math.Clamp(MathF.Abs(pose.GetRotation("Bone").W), -1f, 1f));

        angle.Should().BeApproximately(Degrees(5f), 1e-4f);
    }

    [Fact]
    public void Blend_WithAWeightOfOne_Overwrites()
    {
        AnimationPose pose = new();
        pose.Set("Bone", new Vector3(1f, 1f, 1f), Quaternion.Identity, Vector3.One);

        pose.Blend("Bone", new Vector3(9f, 9f, 9f), Quaternion.Identity, Vector3.One, 1f);

        pose.GetPosition("Bone").Should().Be(new Vector3(9f, 9f, 9f));
    }

    [Fact]
    public void Blend_WithAWeightOfZero_IsANoOp()
    {
        AnimationPose pose = new();
        pose.Set("Bone", new Vector3(1f, 1f, 1f), Quaternion.Identity, Vector3.One);

        pose.Blend("Bone", new Vector3(9f, 9f, 9f), Quaternion.Identity, Vector3.One, 0f);

        pose.GetPosition("Bone").Should().Be(new Vector3(1f, 1f, 1f));
    }

    [Fact]
    public void Blend_AtHalfWeight_LandsHalfwayBetweenTwoClips()
    {
        // One clip at full weight anywhere on the line 0..10, and another at half, is halfway between the two — the
        // property that lets a walk cycle cross-fade into a run.
        AnimationPose pose = new();

        AnimationClip walk = Translate((0f, 0f), (1f, 0f));
        AnimationClip run = Translate((0f, 10f), (1f, 10f));

        walk.SampleAt(0f, pose, 1f);
        run.SampleAt(0f, pose, 0.5f);

        pose.GetPosition("Bone").Y.Should().BeApproximately(5f, 1e-5f);
    }

    [Fact]
    public void Blend_TwoPosesAtHalfWeightEach_IsTheMidpoint()
    {
        AnimationPose first = new();
        first.Set("Bone", new Vector3(0f, 0f, 0f), Quaternion.Identity, Vector3.One);

        AnimationPose second = new();
        second.Set("Bone", new Vector3(4f, 0f, 0f), Quaternion.Identity, Vector3.One);

        AnimationPose blended = new();
        blended.Blend(first, 0.5f);
        blended.Blend(second, 0.5f);

        blended.GetPosition("Bone").X.Should().BeApproximately(2f, 1e-5f);
    }

    [Fact]
    public void Blend_OfAPose_LeavesBonesTheOtherPoseDoesNotHold()
    {
        AnimationPose mine = new();
        mine.Set("Hips", new Vector3(1f, 0f, 0f), Quaternion.Identity, Vector3.One);

        AnimationPose theirs = new();
        theirs.Set("Hand", new Vector3(0f, 5f, 0f), Quaternion.Identity, Vector3.One);

        mine.Blend(theirs, 1f);

        mine.GetPosition("Hips").Should().Be(new Vector3(1f, 0f, 0f));
        mine.GetPosition("Hand").Should().Be(new Vector3(0f, 5f, 0f));
    }

    [Fact]
    public void Get_OfABoneThatIsNotInThePose_IsTheIdentity()
    {
        AnimationPose pose = new();

        pose.GetPosition("Nowhere").Should().Be(Vector3.Zero);
        pose.GetRotation("Nowhere").Should().Be(Quaternion.Identity);
        pose.GetScale("Nowhere").Should().Be(Vector3.One);
        pose.Contains("Nowhere").Should().BeFalse();
    }

    /// <summary>A clip driving one bone's height, with the given keys.</summary>
    private static AnimationClip Translate(params (float Time, float Value)[] keys)
    {
        AnimationCurve curve = new("Bone", AnimationProperty.LocalPosition, AnimationAxis.Y);

        foreach ((float time, float value) in keys)
        {
            curve.AddKey(time, value);
        }

        AnimationClip clip = new("Translate") { WrapMode = AnimationWrapMode.Once };
        clip.AddCurve(curve);

        return clip;
    }

    private static AnimationCurve RotationCurve(AnimationAxis axis, float from, float to)
    {
        AnimationCurve curve = new("Bone", AnimationProperty.LocalRotation, axis);
        curve.AddKey(0f, from);
        curve.AddKey(1f, to);

        return curve;
    }

    private static float Degrees(float value) => value * (MathF.PI / 180f);
}
