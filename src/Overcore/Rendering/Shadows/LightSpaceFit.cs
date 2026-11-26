using Overcore.Components;
using System.Numerics;

namespace Overcore.Rendering.Shadows;

/// <summary>The camera view a cascade has to cover: how wide it opens and how deep the slice is.</summary>
/// <param name="Aspect">Viewport width over height.</param>
/// <param name="FieldOfViewRadians">Vertical field of view, as <see cref="Camera.FieldOfViewRadians"/> gives it.</param>
/// <param name="Near">Depth of the slice's near plane, along the camera's forward axis.</param>
/// <param name="Far">Depth of the slice's far plane, along the same axis.</param>
public readonly record struct SliceGeometry(
    float Aspect,
    float FieldOfViewRadians,
    float Near,
    float Far)
{
    /// <summary>The slice's eight corners in camera space: <c>-Z</c> forward, as <see cref="Camera.GetViewMatrix"/>
    /// builds its view.</summary>
    /// <remarks>
    /// The four sign combinations at each end, in no particular order — the fit takes a min and a max over all
    /// eight, so the order is only ever a test's business. A non-finite or non-positive aspect is treated as 1:1
    /// for the same reason <see cref="Camera.GetProjectionMatrix"/> does it: a viewport that has not been measured
    /// yet must not produce a NaN matrix that then draws a black frame instead of an error.
    /// </remarks>
    public Vector3[] Corners()
    {
        float aspect = float.IsFinite(Aspect) && Aspect > 0f ? Aspect : 1f;
        float halfHeight = MathF.Tan(FieldOfViewRadians * 0.5f);

        return
        [
            .. Corner(aspect, halfHeight, Near),
            .. Corner(aspect, halfHeight, Far)
        ];
    }

    private static Vector3[] Corner(float aspect, float halfHeight, float depth)
    {
        float halfWidth = halfHeight * aspect;

        return
        [
            new Vector3(-halfWidth * depth, -halfHeight * depth, -depth),
            new Vector3(halfWidth * depth, -halfHeight * depth, -depth),
            new Vector3(-halfWidth * depth, halfHeight * depth, -depth),
            new Vector3(halfWidth * depth, halfHeight * depth, -depth)
        ];
    }
}

/// <summary>The sphere that swallows a slice's eight corners.</summary>
/// <param name="Centre">The middle of the slice's box, in world space.</param>
/// <param name="Radius">The distance from <paramref name="Centre"/> to the farthest corner, measured so that it does not
/// vary with where the camera is — see <see cref="LightSpaceFit.Sphere"/>.</param>
public readonly record struct SliceSphere(Vector3 Centre, float Radius);

/// <summary>
/// One cascade's light-space box: the orthographic volume that looks along the light and covers one slice of the
/// camera's view.
/// </summary>
/// <remarks>
/// <para>
/// The box is a <em>cube</em>, not the tightest rectangle the corners fit in. A tight fit has a size and a centre that
/// change with every step the camera takes, and a size that changes per frame cannot be snapped to a texel grid that
/// stays still — which is the whole point of §4.2 of <c>docs/shadows.md</c>. A cube whose half extent comes from the
/// slice's own geometry, with only its centre quantised, gives the same depth map for every camera position inside one
/// step of the grid and then changes in one jump, which is what stops a shadow from shimmering as the camera walks.
/// </para>
/// <para>
/// The eye sits a cube's half extent <em>behind</em> the snapped centre rather than on it, so the slice lies in front of
/// the eye at depths <see cref="NearPlane"/>..<see cref="FarPlane"/>. <see cref="Matrix4x4.CreateOrthographicOffCenter"/>
/// takes positive plane distances and looks down <c>-Z</c>; an eye at the centre would need negative ones and map the
/// near end of the slice to the far end of the depth buffer.
/// </para>
/// </remarks>
public readonly record struct CascadeFit(
    Vector3 Eye,
    Vector3 Forward,
    Vector3 Up,
    Matrix4x4 LightView,
    Matrix4x4 LightProjection,
    Matrix4x4 LightViewProjection,
    float HalfExtent,
    float TexelSize,
    float NearPlane,
    float FarPlane)
{
    /// <summary>Whether a world point falls inside the cascade's box. The property the fit is judged by.</summary>
    public bool Contains(Vector3 worldPoint)
    {
        Vector3 view = Vector3.Transform(worldPoint, LightView);

        // In view space the eye is the origin and looks down -Z, so the box is |x|, |y| <= half and -z within the
        // planes. Written against the planes rather than against HalfExtent so a future non-cube box stays correct.
        return MathF.Abs(view.X) <= HalfExtent
            && MathF.Abs(view.Y) <= HalfExtent
            && -view.Z >= NearPlane
            && -view.Z <= FarPlane;
    }
}

/// <summary>
/// Fits a directional light's orthographic box to one cascade of the camera's view, and keeps that box still.
/// </summary>
/// <remarks>
/// <para>
/// Two published conventions meet here and both are easy to get backwards, so both are named in the code that assumes
/// them. The light's direction <em>to</em> the light is <c>Vector3.Transform(-Vector3.UnitZ, rotation)</c> — the same
/// vector <c>scene.frag</c> dots the surface normal against, and the same one the renderer negates when it uploads
/// <c>uDirLight.direction</c>; <see cref="DirectionToLight"/> is the one place that is written down.
/// <see cref="Matrix4x4.CreateOrthographicOffCenter"/> is right-handed and takes positive plane distances, so the depth
/// it produces rises from the near plane to the far one in the range <c>[0, 1]</c> — which is what the depth texture
/// holds and what the receiver compares against, with no remap in between.
/// </para>
/// <para>
/// No GPU, no engine state: this is arithmetic over matrices and floats, which is what makes every claim in
/// <c>docs/shadows.md</c> §4 and §9 testable in CI.
/// </para>
/// </remarks>
public static class LightSpaceFit
{
    /// <summary>The gap left between the eye and the slice's near face, in world units. Never zero: a point exactly on
    /// the near plane is the one case a depth comparison is allowed to be ambiguous about.</summary>
    public const float EyeMargin = 1f;

    /// <summary>Extra room around the sphere, in texels, so the quantised centre can never push a corner out.</summary>
    private const float SlackTexels = 1f;

    /// <summary>How close to straight up or down the light may point before the up vector has to change.</summary>
    private const float VerticalTolerance = 0.999f;

    /// <summary>
    /// The direction <em>to</em> a light of this rotation: the object's local <c>-Z</c>, normalized.
    /// </summary>
    /// <remarks>
    /// A zero-length result — a rotation that produced a degenerate vector — falls back to straight up. A NaN here
    /// would produce a NaN matrix, and a NaN matrix is a black screen with no error anywhere, which is the failure
    /// mode this whole file exists to keep out of the frame.
    /// </remarks>
    public static Vector3 DirectionToLight(Quaternion lightRotation)
    {
        Vector3 direction = Vector3.Transform(-Vector3.UnitZ, lightRotation);

        return direction.LengthSquared() > 1e-12f ? Vector3.Normalize(direction) : Vector3.UnitY;
    }

    /// <summary>The sphere around the slice's eight corners, in world space.</summary>
    /// <param name="geometry">The slice.</param>
    /// <param name="cameraWorld">The camera's world matrix, which the corners are transformed by.</param>
    /// <remarks>
    /// The centre comes from the world-space corners; the radius does not. It is measured in the camera's own space and
    /// scaled, because the radius has to be <em>exactly</em> the same for any placement of the camera — it is what the
    /// box's size and its texel grid are derived from, and a radius that differs by one ulp between two positions makes
    /// the extent differ by one ulp too, which puts the grid on a slightly different step for no reason.
    ///
    /// Measuring it in world space computes the distance between two large, nearly equal vectors and loses the low bits;
    /// a camera 137 units from the origin is enough to show it. In camera space the corners are near the origin and the
    /// arithmetic is exact, and a scale on the camera is accounted for once, by the basis's longest column.
    /// </remarks>
    public static SliceSphere Sphere(SliceGeometry geometry, Matrix4x4 cameraWorld)
    {
        Vector3 min = new(float.MaxValue);
        Vector3 max = new(float.MinValue);
        Vector3 localMin = new(float.MaxValue);
        Vector3 localMax = new(float.MinValue);

        foreach (Vector3 corner in geometry.Corners())
        {
            Vector3 world = Vector3.Transform(corner, cameraWorld);
            min = Vector3.Min(min, world);
            max = Vector3.Max(max, world);

            localMin = Vector3.Min(localMin, corner);
            localMax = Vector3.Max(localMax, corner);
        }

        Vector3 centre = (min + max) * 0.5f;

        // Half the local box's diagonal is the circumradius of its eight corners, and scaling it by the camera's
        // longest basis column is the same sphere in world space.
        float localRadius = (localMax - localMin).Length() * 0.5f;
        float scale = MathF.Max(
            new Vector3(cameraWorld.M11, cameraWorld.M12, cameraWorld.M13).Length(),
            MathF.Max(
                new Vector3(cameraWorld.M21, cameraWorld.M22, cameraWorld.M23).Length(),
                new Vector3(cameraWorld.M31, cameraWorld.M32, cameraWorld.M33).Length()));

        return new SliceSphere(centre, localRadius * MathF.Max(scale, 1e-6f));
    }

    /// <summary>
    /// Fits a cascade's box to a slice of the camera's view.
    /// </summary>
    /// <param name="geometry">The camera's lens and the slice's depths.</param>
    /// <param name="cameraWorld">The camera's world matrix.</param>
    /// <param name="directionToLight">A direction pointing <em>at</em> the light; see <see cref="DirectionToLight"/>.</param>
    /// <param name="resolution">The shadow map's side, in texels. Fewer than one is raised to one.</param>
    /// <remarks>
    /// The cube's half extent is the slice's bounding-sphere radius plus a texel of slack. It comes from the slice's
    /// geometry — its field of view, aspect and two depths — and never from a fit over corners, so it does not change
    /// as the camera translates. That is what makes the centre's quantisation land on a grid that stays put.
    /// </remarks>
    public static CascadeFit Fit(
        SliceGeometry geometry,
        Matrix4x4 cameraWorld,
        Vector3 directionToLight,
        int resolution)
    {
        int side = Math.Max(1, resolution);

        SliceSphere sphere = Sphere(geometry, cameraWorld);

        Vector3 forward = TravelDirection(directionToLight);
        Vector3 up = UpFor(forward);

        // The light's own axes, built the way `Matrix4x4.CreateLookAt` builds them — its source is
        // `axisZ = normalize(eye - target); axisX = normalize(cross(up, axisZ)); axisY = cross(axisZ, axisX)`.
        //
        // Two details here are not interchangeable, and both were got wrong once while writing this file:
        //
        // * `axisZ` is built from `eye - target`, which is `-forward`, so `cross(up, axisZ)` is the *negation* of
        //   `cross(up, forward)`. A frame assembled from the forward vector directly is mirrored, the centre gets
        //   quantised along the opposite axis, and the slice quietly falls outside a box whose arithmetic looks right.
        // * The three vectors go into *columns*, because `CreateLookAt` transposes its result. Reading them back out as
        //   `(M11, M12, M13)` reads three different vectors' x components, not one axis.
        //
        // Building them here rather than reading them back is the readable form of the same thing; the test asserts the
        // vectors agree with the matrix the view actually uses, which is the part that cannot be taken on trust.
        Vector3 axisZ = -forward;
        Vector3 axisX = Vector3.Normalize(Vector3.Cross(up, axisZ));
        Vector3 axisY = Vector3.Cross(axisZ, axisX);

        // The cube's half extent comes from the slice's geometry, never from a fit over corners, so it does not change
        // as the camera moves — a size that changed per frame could not be snapped to a grid that stayed put.
        //
        // A texel of slack then has to swallow how far quantising the centre can move it. Snapping `x` and `y` moves it
        // by at most half a texel each, and a camera that has not turned leaves the depth unquantised but can still move
        // it by up to a texel (the light is usually oblique, so a camera step is a diagonal step in the light's basis).
        // One texel covers all of that, with the box still a whole number of texels across.
        float radius = MathF.Max(sphere.Radius, 0.0001f);
        float texelSize = (radius * 2f) / side;
        float extent = radius + texelSize * SlackTexels;

        // The texel size the *finished* cube has, which is the one the receiver's normal offset must use: the two
        // differ by the slack, and an offset computed against the wrong one drifts by a fraction of a texel.
        texelSize = (extent * 2f) / side;

        // Quantised in the light's own axes, so the grid is anchored to the light rather than to the camera. Only the
        // centre is snapped; the extent above is what makes the box a whole number of texels across.
        //
        // `(axisX, axisY, axisZ)` is orthonormal, so a point rebuilt from its three coordinates in that basis is the
        // point itself — the quantisation moves the centre and moves nothing else.
        float snappedX = Snap(Vector3.Dot(sphere.Centre, axisX), texelSize);
        float snappedY = Snap(Vector3.Dot(sphere.Centre, axisY), texelSize);

        // Depth is not quantised. It has nothing to gain from a texel grid — the depth buffer's own resolution is what
        // matters there — and quantising it pushes a corner out of the box on the one frame the rounding goes the wrong
        // way. Left alone, it moves by at most the camera's own step, which the cube's slack covers.
        float depth = Vector3.Dot(sphere.Centre, axisZ);

        // `axisZ` points *behind* the eye — it is the view matrix's own z axis, so moving along it moves away from
        // what the box looks at. The eye therefore sits at the snapped centre pushed back along it, which leaves the
        // slice in front of the eye at positive depths, which is what a right-handed orthographic box wants.
        Vector3 centre = axisX * snappedX + axisY * snappedY + axisZ * depth;
        Vector3 eye = centre + axisZ * (extent + EyeMargin);

        Matrix4x4 lightView = Matrix4x4.CreateLookAt(eye, eye + forward, up);

        float near = EyeMargin;
        float far = near + extent * 2f;

        Matrix4x4 lightProjection = Matrix4x4.CreateOrthographicOffCenter(
            -extent, extent, -extent, extent, near, far);

        return new CascadeFit(
            eye,
            forward,
            up,
            lightView,
            lightProjection,
            lightView * lightProjection,
            extent,
            texelSize,
            near,
            far);
    }

    /// <summary>Every cascade of a frame, fitted. The renderer's whole input to the shadow pass.</summary>
    /// <param name="distance">The shadow distance; see <see cref="CascadeSplits.FarDepths"/>.</param>
    /// <param name="cameraNearPlane">The camera's near plane, where the innermost cascade begins.</param>
    /// <param name="aspect">Viewport width over height.</param>
    /// <param name="fieldOfViewRadians">The camera's vertical field of view.</param>
    /// <param name="cameraWorld">The camera's world matrix.</param>
    /// <param name="directionToLight">A direction pointing at the light.</param>
    /// <param name="resolution">The shadow map's side, in texels.</param>
    /// <param name="cascades">Cascade count; see <see cref="CascadeSplits.SupportedCascades"/>.</param>
    /// <param name="splits">Hand-tuned boundaries, or null for the derived ones.</param>
    public static CascadeFit[] FitAll(
        float distance,
        float cameraNearPlane,
        float aspect,
        float fieldOfViewRadians,
        Matrix4x4 cameraWorld,
        Vector3 directionToLight,
        int resolution,
        int cascades = CascadeSplits.DefaultCascades,
        IReadOnlyList<float>? splits = null)
    {
        CascadeRange[] ranges = CascadeSplits.Ranges(distance, cameraNearPlane, cascades, splits);

        CascadeFit[] fits = new CascadeFit[ranges.Length];

        for (int i = 0; i < ranges.Length; i++)
        {
            SliceGeometry geometry = new(aspect, fieldOfViewRadians, ranges[i].Near, ranges[i].Far);
            fits[i] = Fit(geometry, cameraWorld, directionToLight, resolution);
        }

        return fits;
    }

    private static Vector3 TravelDirection(Vector3 directionToLight)
    {
        if (!float.IsFinite(directionToLight.X)
            || !float.IsFinite(directionToLight.Y)
            || !float.IsFinite(directionToLight.Z)
            || directionToLight.LengthSquared() <= 1e-12f)
        {
            // Straight down: a light with no direction still has to produce a matrix, and a NaN one is a black frame.
            return -Vector3.UnitY;
        }

        return -Vector3.Normalize(directionToLight);
    }

    private static Vector3 UpFor(Vector3 forward)
    {
        // A light pointing straight down (or up) leaves `CreateLookAt` with nothing to cross the axis with, and the
        // matrix it returns is full of NaN — which renders as a black screen rather than as an error.
        return MathF.Abs(forward.Y) > VerticalTolerance ? Vector3.UnitZ : Vector3.UnitY;
    }

    private static float Snap(float value, float step)
    {
        return float.IsFinite(value) && step > 0f ? MathF.Round(value / step) * step : 0f;
    }
}
