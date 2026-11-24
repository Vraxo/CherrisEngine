using Cherris;
using Cherris.Components;
using System.Numerics;

namespace CherrisEditorWeb;

/// <summary>One frame of viewport input, in the terms a fly camera needs.</summary>
/// <param name="Look">Pointer movement in viewport pixels, as a look drag produced it.</param>
/// <param name="Move">Local axes: X is right (D positive), Z is back (W negative).</param>
/// <param name="Vertical">World up or down: E adds, Q subtracts.</param>
/// <param name="Zoom">Wheel notches; positive travels the way the camera looks.</param>
/// <param name="Run">Shift is held, so movement is faster.</param>
public readonly record struct ViewportCameraInput(
    Vector2 Look,
    Vector2 Move,
    float Vertical,
    float Zoom,
    bool Run)
{
    /// <summary>Nothing asked for: the camera stays exactly where it is.</summary>
    public static ViewportCameraInput None { get; } = new(Vector2.Zero, Vector2.Zero, 0f, 0f, false);
}

/// <summary>
/// The editor's fly camera: right-drag to look, WASD to move, Q and E down and up, the wheel to fly along the view.
/// </summary>
/// <remarks>
/// <para>
/// The feel is the desktop editor's (<c>src/CherrisEditor/Core/EditorController.cs</c>), because the two editors
/// should be the same instrument: 0.0015 radians of turn per pixel of drag, 5 units per second at a walk and three
/// times that with Shift, half a unit per wheel notch, and pitch that stops just short of straight up or down so the
/// view can never roll over.
/// </para>
/// <para>
/// Movement uses the yaw only, never the pitch: looking down and pressing W walks forward across the floor rather
/// than diving into it, which is what makes the camera usable for laying a scene out.
/// </para>
/// </remarks>
public sealed class ViewportCameraController
{
    /// <summary>Units per second the camera travels at a walk.</summary>
    public const float MoveSpeed = 5f;

    /// <summary>Radians of turn per viewport pixel of drag.</summary>
    public const float MouseSensitivity = 0.0015f;

    /// <summary>Units travelled per wheel notch.</summary>
    public const float ZoomSensitivity = 0.5f;

    /// <summary>What holding Shift multiplies movement by.</summary>
    public const float RunMultiplier = 3f;

    /// <summary>How close to straight up or down the pitch may get, in radians.</summary>
    public const float PitchLimit = (MathF.PI / 2f) - 0.001f;

    private float _yaw;
    private float _pitch;

    /// <summary>The direction the camera looks in, as yaw then pitch in radians — the scene's own convention.</summary>
    public Vector2 Look => new(_yaw, _pitch);

    /// <summary>
    /// Takes the camera's current rotation as the starting point, so the first drag continues from where the scene
    /// left the camera instead of snapping back to the last pose this controller saw.
    /// </summary>
    /// <param name="camera">The camera about to be flown.</param>
    public void SyncFrom(Camera camera)
    {
        ArgumentNullException.ThrowIfNull(camera);

        if (camera.GameObject is { } owner)
        {
            SyncFrom(owner.Transform.Rotation);
        }
    }

    /// <summary>Takes a rotation as the starting point. A rotation that has no usable angles is ignored.</summary>
    /// <param name="rotation">The rotation to read yaw and pitch from.</param>
    public void SyncFrom(Quaternion rotation)
    {
        Vector3 euler = EngineMath.ToEulerAngles(rotation);

        if (float.IsFinite(euler.X) && float.IsFinite(euler.Y))
        {
            _pitch = Math.Clamp(euler.X, -PitchLimit, PitchLimit);
            _yaw = MathF.IEEERemainder(euler.Y, MathF.Tau);
        }
    }

    /// <summary>Applies one frame of input to the camera. A camera that is on no object, or absent, is ignored.</summary>
    /// <param name="camera">The camera to fly.</param>
    /// <param name="input">What this frame asks for.</param>
    /// <param name="deltaTime">Seconds since the last frame; a value that is not a positive number moves nothing.</param>
    public void Update(Camera? camera, in ViewportCameraInput input, float deltaTime)
    {
        if (camera?.GameObject is not { } owner)
        {
            return;
        }

        if (!float.IsFinite(deltaTime) || deltaTime < 0f)
        {
            deltaTime = 0f;
        }

        Transform transform = owner.Transform;

        if (input.Look != Vector2.Zero && float.IsFinite(input.Look.X) && float.IsFinite(input.Look.Y))
        {
            _yaw = MathF.IEEERemainder(_yaw - (input.Look.X * MouseSensitivity), MathF.Tau);
            _pitch = Math.Clamp(_pitch - (input.Look.Y * MouseSensitivity), -PitchLimit, PitchLimit);

            // Yaw first and pitch second, which is how the scene loader reads a rotation back out of a file.
            transform.Rotation = Quaternion.CreateFromYawPitchRoll(_yaw, _pitch, 0f);
        }

        if (float.IsFinite(input.Zoom) && input.Zoom != 0f)
        {
            transform.Position += Vector3.Transform(-Vector3.UnitZ, transform.Rotation) * (input.Zoom * ZoomSensitivity);
        }

        Vector3 direction = MoveDirection(input);

        if (direction != Vector3.Zero && deltaTime > 0f)
        {
            float speed = MoveSpeed * (input.Run ? RunMultiplier : 1f);
            transform.Position += direction * (speed * deltaTime);
        }
    }

    /// <summary>The direction to move in, unit length, from the held keys. Zero when nothing is held.</summary>
    private Vector3 MoveDirection(in ViewportCameraInput input)
    {
        Vector2 move = input.Move;
        float vertical = input.Vertical;

        if ((move == Vector2.Zero && vertical == 0f) ||
            !float.IsFinite(move.X) || !float.IsFinite(move.Y) || !float.IsFinite(vertical))
        {
            return Vector3.Zero;
        }

        // Horizontal movement follows the yaw alone, so looking down does not turn W into a dive.
        Vector3 horizontal = Vector3.Transform(
            new Vector3(move.X, 0f, move.Y),
            Quaternion.CreateFromAxisAngle(Vector3.UnitY, _yaw));

        Vector3 direction = new(horizontal.X, vertical, horizontal.Z);
        float lengthSquared = direction.LengthSquared();

        return float.IsFinite(lengthSquared) && lengthSquared > 1e-8f
            ? direction / MathF.Sqrt(lengthSquared)
            : Vector3.Zero;
    }
}
