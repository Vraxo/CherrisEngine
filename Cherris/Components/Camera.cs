using System.Numerics;

namespace Cherris.Components;

public class Camera : Component
{
    public float FieldOfView { get; set; } = 60.0f; // in degrees
    public float NearClipPlane { get; set; } = 0.1f;
    public float FarClipPlane { get; set; } = 100.0f;

    public Matrix4x4 GetViewMatrix()
    {
        // Assumes camera looks along its local -Z axis
        Transform transform = GameObject.Transform;
        Vector3 lookAt = transform.Position + Vector3.Transform(-Vector3.UnitZ, transform.Rotation);
        Vector3 up = Vector3.Transform(Vector3.UnitY, transform.Rotation);

        return Matrix4x4.CreateLookAt(transform.Position, lookAt, up);
    }

    public Matrix4x4 GetProjectionMatrix(float aspectRatio)
    {
        return Matrix4x4.CreatePerspectiveFieldOfView(
            FieldOfView * (MathF.PI / 180.0f),
            aspectRatio,
            NearClipPlane,
            FarClipPlane);
    }
}