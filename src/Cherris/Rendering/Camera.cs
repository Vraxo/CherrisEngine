using System.Numerics;

namespace Cherris.Components;

/// <summary>
/// A perspective camera: the clip planes and field of view a scene is drawn through.
/// </summary>
/// <remarks>
/// <para>
/// The three values are clamped as they are written, because a camera that is out of range is not a slightly
/// wrong picture, it is a renderer that throws: OpenTK's <c>CreatePerspectiveFieldOfView</c> rejects a negative
/// or zero near plane, and the render loop dies with it. Anything that can reach a camera — the inspector's
/// number field, a drag, a reset, a scene file, a script — goes through these setters, so the values here are
/// ones a projection can always be built from.
/// </para>
/// <para>
/// The rule the frustum is kept in: the near plane is at least <see cref="MinimumNearClipPlane"/> and the far
/// plane is at least twice as far as the near one. A value that is not a number at all is ignored rather than
/// clamped, since clamping NaN would store NaN.
/// </para>
/// </remarks>
public class Camera : Component
{
    /// <summary>The closest a near plane may be to the eye, in world units.</summary>
    public const float MinimumNearClipPlane = 0.001f;

    /// <summary>The narrowest field of view, in degrees. A perspective projection wants more than nothing.</summary>
    public const float MinimumFieldOfView = 1f;

    /// <summary>The widest field of view, in degrees: below a half turn, or the matrix flips.</summary>
    public const float MaximumFieldOfView = 179f;

    private float _fieldOfView = 60f;
    private float _nearClipPlane = 0.1f;
    private float _farClipPlane = 100f;

    /// <summary>Vertical field of view, in degrees, kept between 1 and 179.</summary>
    public float FieldOfView
    {
        get => _fieldOfView;
        set
        {
            if (float.IsFinite(value))
            {
                _fieldOfView = Math.Clamp(value, MinimumFieldOfView, MaximumFieldOfView);
            }
        }
    }

    /// <summary>
    /// Distance to the near clip plane, in world units, kept at or above
    /// <see cref="MinimumNearClipPlane"/>. Pushing it past the far plane moves the far plane out with it.
    /// </summary>
    public float NearClipPlane
    {
        get => _nearClipPlane;
        set
        {
            if (!float.IsFinite(value))
            {
                return;
            }

            _nearClipPlane = MathF.Max(value, MinimumNearClipPlane);

            if (_farClipPlane <= _nearClipPlane)
            {
                // No frustum at all otherwise: the far plane is kept twice as deep as the near one.
                _farClipPlane = _nearClipPlane * 2f;
            }
        }
    }

    /// <summary>
    /// Distance to the far clip plane, in world units, kept at least twice the near plane so the frustum always
    /// has depth.
    /// </summary>
    public float FarClipPlane
    {
        get => _farClipPlane;
        set
        {
            if (float.IsFinite(value))
            {
                _farClipPlane = MathF.Max(value, _nearClipPlane * 2f);
            }
        }
    }

    /// <summary>The vertical field of view in radians, which is the unit a projection matrix takes.</summary>
    public float FieldOfViewRadians => _fieldOfView * (MathF.PI / 180f);

    public Matrix4x4 GetViewMatrix()
    {
        // Assumes camera looks along its local -Z axis
        Transform transform = GameObject.Transform;
        Vector3 lookAt = transform.Position + Vector3.Transform(-Vector3.UnitZ, transform.Rotation);
        Vector3 up = Vector3.Transform(Vector3.UnitY, transform.Rotation);

        return Matrix4x4.CreateLookAt(transform.Position, lookAt, up);
    }

    /// <summary>Builds the projection for a viewport of the given aspect ratio.</summary>
    /// <param name="aspectRatio">Width over height. Anything that is not a positive number is drawn as 1:1.</param>
    public Matrix4x4 GetProjectionMatrix(float aspectRatio)
    {
        // The clip planes are already valid; the aspect is the one thing a caller can get wrong, and a zero or
        // NaN one would produce a matrix full of NaNs rather than a picture.
        float aspect = float.IsFinite(aspectRatio) && aspectRatio > 0f ? aspectRatio : 1f;

        return Matrix4x4.CreatePerspectiveFieldOfView(FieldOfViewRadians, aspect, NearClipPlane, FarClipPlane);
    }
}
