using AwesomeAssertions;
using Overcore.Components;
using Overcore.Rendering.Shadows;
using System.Numerics;
using Xunit;

namespace Overcore.Tests.Rendering;

/// <summary>
/// The light-space fit: the orthographic box one cascade is drawn through, and the property that keeps a shadow from
/// crawling as the camera moves. Everything here is arithmetic over matrices, so the whole thing is asserted exactly —
/// the draw itself is the only part of the shadow layer a green run does not prove.
/// </summary>
public class LightSpaceFitTests
{
    private const int Resolution = 2048;

    private static readonly float FieldOfView = 60f * (MathF.PI / 180f);

    /// <summary>
    /// A camera at a boring place: no rotation, so it looks down world -Z like the engine's cameras, and the world-space
    /// corners of a slice are its local ones plus the translation.
    /// </summary>
    private static Matrix4x4 CameraAt(float x = 0f, float y = 2f, float z = 0f)
    {
        return Matrix4x4.CreateTranslation(x, y, z);
    }

    /// <summary>A light over the camera's shoulder and a little to its left: the ordinary outdoor case.</summary>
    private static readonly Vector3 OverTheShoulder = Vector3.Normalize(new Vector3(0.3f, 0.9f, 0.4f));

    private static SliceGeometry Slice(float near = 0.1f, float far = 10f, float aspect = 16f / 9f)
    {
        return new SliceGeometry(aspect, FieldOfView, near, far);
    }

    // ---------------------------------------------------------------- the slice

    [Fact]
    public void ASliceHasEightCorners()
    {
        Slice(0.1f, 10f).Corners().Should().HaveCount(8);
    }

    [Fact]
    public void TheCornersAreInFrontOfTheCameraOnItsNegativeZAxis()
    {
        // The engine's cameras look down local -Z (`Camera.GetViewMatrix` builds its target that way), so a corner with
        // a positive z would be behind the camera and the whole frustum geometry would be the mirror of itself.
        Slice(1f, 10f).Corners().Should().OnlyContain(corner => corner.Z < 0f && corner.Z >= -10.0001f);
    }

    [Fact]
    public void TheFarPlaneIsWiderThanTheNearOne()
    {
        // A perspective slice widens with depth; a fit that assumed a box would shadow-gap the corners of the far plane.
        Vector3[] corners = Slice(1f, 10f).Corners();

        float nearWidth = corners.Where(corner => MathF.Abs(corner.Z + 1f) < 0.001f).Max(corner => MathF.Abs(corner.X));
        float farWidth = corners.Where(corner => MathF.Abs(corner.Z + 10f) < 0.001f).Max(corner => MathF.Abs(corner.X));

        farWidth.Should().BeApproximately(nearWidth * 10f, 0.001f);
    }

    [Fact]
    public void AnUnmeasurableAspect_IsTreatedAsSquare()
    {
        // A viewport that has not been measured yet writes 0, and a NaN matrix is a black frame instead of an error.
        Slice(aspect: 0f).Corners().Should().Equal(Slice(aspect: 1f).Corners());
    }

    [Fact]
    public void TheSphereIsBigEnoughToHoldTheSlice()
    {
        // The box is sized from this sphere, so a sphere that did not hold the corners would be a box that did not
        // either — the failure would show up as a clipped shadow rather than as an error, which is why it is checked
        // here as well as through `Contains`.
        SliceGeometry geometry = Slice(0.1f, 10f);
        Matrix4x4 camera = CameraAt();
        SliceSphere sphere = LightSpaceFit.Sphere(geometry, camera);

        foreach (Vector3 corner in geometry.Corners())
        {
            Vector3 world = Vector3.Transform(corner, camera);

            Vector3.Distance(world, sphere.Centre).Should().BeLessThanOrEqualTo(sphere.Radius + 0.0001f);
        }
    }

    // ---------------------------------------------------------------- the containment property

    [Theory]
    [InlineData("default light", 0.1f, 10f)]
    [InlineData("far slice", 70f, 150f)]
    [InlineData("near slice", 0.1f, 0.5f)]
    [InlineData("very near slice", 0.1f, 1f)]
    public void EveryCornerOfTheSliceIsInsideTheBox(string name, float near, float far)
    {
        // The one thing a cascade must do. A corner outside the box is geometry with no shadow, and it shows up as a
        // caster whose shadow is clipped to a rectangle at the edge of the screen.
        Matrix4x4 camera = CameraAt();
        SliceGeometry geometry = Slice(near, far);
        CascadeFit fit = LightSpaceFit.Fit(geometry, camera, OverTheShoulder, Resolution);

        foreach (Vector3 corner in geometry.Corners())
        {
            Vector3 world = Vector3.Transform(corner, camera);

            fit.Contains(world).Should().BeTrue($"{name}: {corner} should be inside the box");
        }
    }

    [Theory]
    [InlineData("steep light", 0.2f, 0.9f, 0.1f)]
    [InlineData("shallow light", 0.1f, 0.14f, 0.98f)]
    [InlineData("light straight down", 0f, -1f, 0f)]
    [InlineData("light straight up", 0f, 1f, 0f)]
    [InlineData("light from the side", 1f, 0.01f, 0.01f)]
    [InlineData("light from behind", -1f, 0.3f, 0.2f)]
    public void EveryCornerIsInsideTheBoxAtAnyLightAngle(string name, float x, float y, float z)
    {
        Matrix4x4 camera = CameraAt();
        SliceGeometry geometry = Slice();
        CascadeFit fit = LightSpaceFit.Fit(geometry, camera, Vector3.Normalize(new Vector3(x, y, z)), Resolution);

        foreach (Vector3 corner in geometry.Corners())
        {
            fit.Contains(Vector3.Transform(corner, camera))
                .Should().BeTrue($"{name}: {corner} should be inside the box");
        }
    }

    [Fact]
    public void APointBesideTheSliceIsOutsideTheBox()
    {
        // The complement of the test above, so a Contains that returned true unconditionally cannot pass both.
        Matrix4x4 camera = CameraAt();
        CascadeFit fit = LightSpaceFit.Fit(Slice(0.1f, 10f), camera, OverTheShoulder, Resolution);

        Vector3 beside = Vector3.Transform(new Vector3(500f, 0f, -5f), camera);

        fit.Contains(beside).Should().BeFalse();
    }

    [Fact]
    public void TheDepthTheBoxRecordsIsInTheUnitRange()
    {
        // The receiver compares against the depth texture's [0, 1]. Anything outside it is a corner that is inside the
        // x/y square but clipped out of the depth buffer, which looks exactly like a caster that casts nothing.
        Matrix4x4 camera = CameraAt();
        SliceGeometry geometry = Slice(0.1f, 10f);
        CascadeFit fit = LightSpaceFit.Fit(geometry, camera, OverTheShoulder, Resolution);

        foreach (Vector3 corner in geometry.Corners())
        {
            Vector4 clip = Vector4.Transform(new Vector4(Vector3.Transform(corner, camera), 1f), fit.LightViewProjection);
            float depth = clip.Z / clip.W;

            depth.Should().BeInRange(-0.0001f, 1.0001f);
        }
    }

    [Fact]
    public void ABoxBuiltFromANaNDirectionStillProducesAFiniteMatrix()
    {
        // A NaN matrix is a black screen with no error anywhere, which is the failure mode the whole file guards.
        CascadeFit fit = LightSpaceFit.Fit(Slice(), CameraAt(), new Vector3(float.NaN), Resolution);

        Finite(fit.LightViewProjection).Should().BeTrue("a NaN matrix is a black frame with no error anywhere");
    }

    [Fact]
    public void AZeroDirectionStillProducesAFiniteMatrix()
    {
        CascadeFit fit = LightSpaceFit.Fit(Slice(), CameraAt(), Vector3.Zero, Resolution);

        Finite(fit.LightViewProjection).Should().BeTrue("a NaN matrix is a black frame with no error anywhere");
    }

    [Fact]
    public void AResolutionBelowOneIsRaisedToOne()
    {
        // The builder would refuse a zero-width texture; the fit should not be the thing that finds out.
        CascadeFit fit = LightSpaceFit.Fit(Slice(), CameraAt(), OverTheShoulder, 0);

        fit.TexelSize.Should().BeGreaterThan(0f);
        Finite(fit.LightViewProjection).Should().BeTrue("a NaN matrix is a black frame with no error anywhere");
    }

    // ---------------------------------------------------------------- the direction convention

    [Fact]
    public void TheLightTravelsAwayFromTheSun()
    {
        // The renderer uploads "direction to light" to the shader; the fit needs where the light *goes*. Getting this
        // backwards points every shadow away from the sun, which looks correct in a scene with one caster.
        Quaternion facingDown = Quaternion.CreateFromAxisAngle(Vector3.UnitX, MathF.PI / 2f);

        Vector3 toLight = LightSpaceFit.DirectionToLight(facingDown);

        // The object's own -Z, as scene.frag dots against.
        Approximately(Vector3.Transform(toLight, Quaternion.Inverse(facingDown)), -Vector3.UnitZ).Should().BeTrue();
    }

    [Fact]
    public void AZeroRotationLooksStraightDownTheForwardAxis()
    {
        Approximately(LightSpaceFit.DirectionToLight(Quaternion.Identity), -Vector3.UnitZ).Should().BeTrue();
    }

    [Fact]
    public void TheBoxLooksDownTheLightNotBackAtIt()
    {
        // `axisZ` points at the sun and `forward` away from it; the eye sits behind the slice along `axisZ`. If the
        // matrix were built the other way round every caster would be behind the eye and nothing would be recorded.
        CascadeFit fit = LightSpaceFit.Fit(Slice(), CameraAt(), OverTheShoulder, Resolution);

        fit.Contains(Vector3.Transform(new Vector3(0f, 0f, -5f), CameraAt())).Should().BeTrue();
        Vector3 atTheEyeProjected = Vector3.Transform(fit.Eye, fit.LightView);

        atTheEyeProjected.Length().Should().BeLessThan(0.001f, "a view matrix puts its eye at the origin");
    }

    [Fact]
    public void ALightStraightDownAndOneStraightUpBothFit()
    {
        // The degenerate up vector is the one case `CreateLookAt` cannot survive: with the world up parallel to the axis
        // the cross product is zero and the matrix it returns is all NaN.
        Matrix4x4 camera = CameraAt();

        foreach (Vector3 direction in new[] { -Vector3.UnitY, Vector3.UnitY })
        {
            CascadeFit fit = LightSpaceFit.Fit(Slice(), camera, direction, Resolution);

            Finite(fit.LightViewProjection).Should().BeTrue("a NaN matrix is a black frame with no error anywhere");
            fit.Contains(Vector3.Transform(new Vector3(0f, 0f, -5f), camera)).Should().BeTrue();
        }
    }

    // ---------------------------------------------------------------- texel snapping

    [Fact]
    public void TheExtentDoesNotDependOnWhereTheCameraIs()
    {
        // The property a min/max fit over corners breaks. A half extent that changes every frame cannot be snapped to a
        // grid that stays put — the grid would move with it and every shadow edge would crawl.
        SliceGeometry geometry = Slice();
        CascadeFit origin = LightSpaceFit.Fit(geometry, CameraAt(), OverTheShoulder, Resolution);

        foreach (float x in new[] { 0.001f, 0.5f, 3f, 137f })
        {
            CascadeFit moved = LightSpaceFit.Fit(geometry, CameraAt(x), OverTheShoulder, Resolution);

            moved.HalfExtent.Should().Be(origin.HalfExtent, "the box's size comes from the slice, not from the camera");
            moved.TexelSize.Should().Be(origin.TexelSize);
        }
    }

    [Fact]
    public void TheExtentDoesNotDependOnWhereTheCameraIsForAnySlice()
    {
        // Rotation changes the slice's world footprint, so the sphere is rebuilt — but its radius is a function of the
        // lens and the two depths alone, and a rotated camera must not change it either.
        foreach ((float near, float far) in new[] { (0.1f, 10f), (70f, 150f), (0.1f, 0.2f) })
        {
            SliceGeometry geometry = Slice(near, far);
            CascadeFit still = LightSpaceFit.Fit(geometry, CameraAt(), OverTheShoulder, Resolution);

            Matrix4x4 turned = Matrix4x4.CreateFromYawPitchRoll(0.7f, 0.2f, 0.1f) * Matrix4x4.CreateTranslation(40f, 2f, -9f);
            CascadeFit rotated = LightSpaceFit.Fit(geometry, turned, OverTheShoulder, Resolution);

            // A thousandth of a unit, because the extent is about 13 and float32 resolves that to 1e-6.
            rotated.HalfExtent.Should().BeApproximately(still.HalfExtent, 0.001f);
        }
    }

    [Fact]
    public void TheSnappedCoordinateIsAStaircaseAndNeverAFraction()
    {
        // The crawl in one assertion. As the camera moves, the grid coordinate holds still and then steps by exactly one
        // whole texel: a depth map anchored that way is identical for a whole band of camera positions. A coordinate that
        // took fractional values would be a map sliding by a fraction of a texel every frame — a shadow that shimmers as
        // the camera walks.
        //
        // Sweeping the camera and inspecting the sequence is the honest form of the test: where the slice's centre falls
        // inside a texel depends on the scene, so a case that predicted "half a texel of movement moves the grid once"
        // would be asserting something about the fixtures rather than about the fit.
        SliceGeometry geometry = Slice();
        CascadeFit atOrigin = LightSpaceFit.Fit(geometry, CameraAt(), OverTheShoulder, Resolution);

        Vector3 axisX = AxisOf(atOrigin, 0);
        float texel = atOrigin.TexelSize;

        float CoordinateAt(float cameraTexels)
        {
            Matrix4x4 camera = Matrix4x4.CreateTranslation(axisX * (texel * cameraTexels)) * CameraAt();
            CascadeFit fit = LightSpaceFit.Fit(geometry, camera, OverTheShoulder, Resolution);

            return Vector3.Dot(fit.Eye, AxisOf(fit, 0)) / fit.TexelSize;
        }

        float[] samples = [.. Enumerable.Range(0, 301).Select(index => CoordinateAt(index * 0.01f))];

        // A hundredth of a texel is the tolerance, not an exact zero, and the reason is float32 rather than sloppiness:
        // the coordinate is a dot product of world positions, so at a few hundred texels from the origin its last bit is
        // worth about 1.5e-5 texels. A real fraction that would mean a bug — an unsnapped centre, or a snap applied in
        // the wrong space — is off by up to half a texel, which is fifty times this tolerance.
        const float Tick = 0.01f;

        foreach (float sample in samples)
        {
            MathF.Abs(sample - MathF.Round(sample)).Should().BeLessThan(Tick, $"{sample} is not a whole texel");
        }

        // Both axes, at one arbitrary position, because "snapped" has to mean exactly that: a centre merely close to a
        // texel boundary still slides by a fraction of one, which is the crawl this exists to remove.
        CascadeFit anywhere = LightSpaceFit.Fit(geometry, CameraAt(0.37f), OverTheShoulder, Resolution);

        foreach (int axis in new[] { 0, 1 })
        {
            float texels = Vector3.Dot(anywhere.Eye, AxisOf(anywhere, axis)) / anywhere.TexelSize;

            MathF.Abs(texels - MathF.Round(texels)).Should().BeLessThan(Tick);
        }

        List<float> jumps = [];

        for (int i = 1; i < samples.Length; i++)
        {
            float jump = MathF.Round(samples[i]) - MathF.Round(samples[i - 1]);

            // One texel at a time, and never a fraction of one.
            (jump == 0f || jump == 1f).Should().BeTrue($"the grid stepped {jump}");
            jumps.Add(jump);
        }

        jumps.Should().Contain(1f, "three texels of travel has to cross a boundary somewhere");
        jumps.Should().Contain(0f, "and most of that travel is inside a texel, which is the point");
    }

    [Fact]
    public void TheDepthOfTheCentreIsNotQuantised()
    {
        // Depth gains nothing from a texel grid — the depth buffer's own resolution is what matters there — and
        // quantising it is what pushes a corner out of the box on the one frame the rounding goes the wrong way.
        SliceGeometry geometry = Slice();
        CascadeFit a = LightSpaceFit.Fit(geometry, CameraAt(), OverTheShoulder, Resolution);
        CascadeFit b = LightSpaceFit.Fit(geometry, CameraAt(0.001f), OverTheShoulder, Resolution);

        float aDepth = Vector3.Dot(a.Eye, AxisOf(a, 2));
        float bDepth = Vector3.Dot(b.Eye, AxisOf(b, 2));

        bDepth.Should().NotBe(aDepth, "the camera moved along the light, so the depth moved with it");
    }

    [Fact]
    public void FittingEveryCascadeCoversTheWholeShadowDistance()
    {
        // The renderer's one call. Every cascade has to be usable: a finite matrix, a positive extent and a slice it
        // actually contains.
        CascadeFit[] fits = LightSpaceFit.FitAll(
            distance: 150f,
            cameraNearPlane: 0.1f,
            aspect: 16f / 9f,
            fieldOfViewRadians: FieldOfView,
            cameraWorld: CameraAt(),
            directionToLight: OverTheShoulder,
            resolution: Resolution);

        fits.Should().HaveCount(4);

        CascadeRange[] ranges = CascadeSplits.Ranges(150f, 0.1f, 4);

        for (int i = 0; i < fits.Length; i++)
        {
            Finite(fits[i].LightViewProjection).Should().BeTrue();
            fits[i].HalfExtent.Should().BeGreaterThan(0f);

            SliceGeometry geometry = new(16f / 9f, FieldOfView, ranges[i].Near, ranges[i].Far);

            foreach (Vector3 corner in geometry.Corners())
            {
                fits[i].Contains(Vector3.Transform(corner, CameraAt())).Should().BeTrue($"cascade {i} covers its slice");
            }
        }
    }

    [Fact]
    public void TheBoxesXAxisIsTheNegationOfCrossUpForward()
    {
        // `CreateLookAt` builds `axisZ` from `eye - target`, which is `-forward`, so `axisX` comes out as the *negation*
        // of `cross(up, forward)`. A centre quantised in a basis built from `forward` directly is offset along the
        // opposite axis and the slice falls outside a box whose arithmetic looks perfectly reasonable — this assertion
        // is here because that exact mistake was made while writing the fit.
        CascadeFit fit = LightSpaceFit.Fit(Slice(), CameraAt(), OverTheShoulder, Resolution);

        Vector3 naive = Vector3.Normalize(Vector3.Cross(fit.Up, fit.Forward));

        Approximately(AxisOf(fit, 0), -naive).Should().BeTrue();
    }

    [Fact]
    public void TheBoxesThreeAxesAreOrthonormal()
    {
        // The snapping relies on it: a coordinate read in a basis that is not orthonormal does not rebuild the point it
        // came from, so "the centre moved by exactly one texel" would be false while the arithmetic still type-checked.
        CascadeFit fit = LightSpaceFit.Fit(Slice(70f, 150f), CameraAt(133f), OverTheShoulder, Resolution);

        Vector3 x = AxisOf(fit, 0);
        Vector3 y = AxisOf(fit, 1);
        Vector3 z = AxisOf(fit, 2);

        x.Length().Should().BeApproximately(1f, 0.0001f);
        y.Length().Should().BeApproximately(1f, 0.0001f);
        z.Length().Should().BeApproximately(1f, 0.0001f);

        Vector3.Dot(x, y).Should().BeApproximately(0f, 0.0001f);
        Vector3.Dot(x, z).Should().BeApproximately(0f, 0.0001f);
        Vector3.Dot(y, z).Should().BeApproximately(0f, 0.0001f);
    }

    [Fact]
    public void TheInnerCascadeIsFinerThanTheOuterOne()
    {
        // The reason there are four: the cascade nearest the camera is the one the eye judges contact by, and it has to
        // have the smaller texel.
        CascadeFit[] fits = LightSpaceFit.FitAll(
            150f, 0.1f, 16f / 9f, FieldOfView, CameraAt(), OverTheShoulder, Resolution);

        fits[0].TexelSize.Should().BeLessThan(fits[^1].TexelSize);
    }

    // ---------------------------------------------------------------- helpers

    /// <summary>
    /// The light's x, y or z axis, read back out of the view matrix. `CreateLookAt` transposes its result, so the axes
    /// live in columns 0..2 of rows 0..2 — reading `(M11, M12, M13)` would read three different vectors' x components.
    /// </summary>
    private static Vector3 AxisOf(CascadeFit fit, int axis)
    {
        return axis switch
        {
            0 => new Vector3(fit.LightView.M11, fit.LightView.M21, fit.LightView.M31),
            1 => new Vector3(fit.LightView.M12, fit.LightView.M22, fit.LightView.M32),
            _ => new Vector3(fit.LightView.M13, fit.LightView.M23, fit.LightView.M33)
        };
    }

    /// <summary>Whether two directions agree to within a ten-thousandth on every component.</summary>
    private static bool Approximately(Vector3 actual, Vector3 expected)
    {
        return MathF.Abs(actual.X - expected.X) < 0.0001f
            && MathF.Abs(actual.Y - expected.Y) < 0.0001f
            && MathF.Abs(actual.Z - expected.Z) < 0.0001f;
    }

    /// <summary>Whether all sixteen entries are finite. A NaN anywhere in a matrix renders as a black frame silently.</summary>
    private static bool Finite(Matrix4x4 matrix)
    {
        return float.IsFinite(matrix.M11) && float.IsFinite(matrix.M12) && float.IsFinite(matrix.M13) && float.IsFinite(matrix.M14)
            && float.IsFinite(matrix.M21) && float.IsFinite(matrix.M22) && float.IsFinite(matrix.M23) && float.IsFinite(matrix.M24)
            && float.IsFinite(matrix.M31) && float.IsFinite(matrix.M32) && float.IsFinite(matrix.M33) && float.IsFinite(matrix.M34)
            && float.IsFinite(matrix.M41) && float.IsFinite(matrix.M42) && float.IsFinite(matrix.M43) && float.IsFinite(matrix.M44);
    }
}
