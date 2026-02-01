using Cherris.Components;
using Cherris.Core;
using Jitter.Collision.Shapes;
using System.Numerics;

namespace Cherris.Rendering.OpenTK;

internal class OpenGLGizmoRenderer
{
    private readonly OpenGLDebugRenderer _debugRenderer;

    private static readonly Vector3 DirectionalLightColor = new(1.0f, 0.9f, 0.2f);
    private static readonly Vector3 SpotlightColor = new(1.0f, 0.9f, 0.2f);
    private static readonly Vector3 StaticBodyColor = new(0.2f, 0.8f, 0.2f);
    private static readonly Vector3 DynamicBodyColor = new(0.8f, 0.2f, 0.8f);

    private const float GizmoScale = 2.0f;
    private const float ArrowHeadSize = 0.25f;
    private const float ArrowLength = 1.0f * GizmoScale;
    private const float CircleRadius = 0.5f * GizmoScale;
    private const int CircleSegments = 16;

    public OpenGLGizmoRenderer(OpenGLDebugRenderer debugRenderer)
    {
        _debugRenderer = debugRenderer;
    }

    public void Render(IEnumerable<GameObject> gameObjects, GameObject? selectedObject, bool showPhysicsColliders)
    {
        _debugRenderer.Clear();

        if (selectedObject?.GetComponent<Light>() is Light light)
        {
            DrawLightGizmo(light);
        }

        if (!showPhysicsColliders)
        {
            return;
        }

        foreach (GameObject gameObject in gameObjects)
        {
            DrawRigidBodyGizmo(gameObject);
        }
    }

    private void DrawLightGizmo(Light light)
    {
        switch (light.Type)
        {
            case LightType.Directional:
                DrawDirectionalLightGizmo(light);
                break;
            case LightType.Spot:
                DrawSpotlightGizmo(light);
                break;
        }
    }

    private void DrawDirectionalLightGizmo(Light light)
    {
        if (light.GameObject is null)
        {
            return;
        }

        Transform transform = light.GameObject.Transform;
        Vector3 color = DirectionalLightColor;

        Vector3 direction = Vector3.Transform(-Vector3.UnitZ, transform.Rotation);
        Vector3 up = Vector3.Transform(Vector3.UnitY, transform.Rotation);
        Vector3 right = Vector3.Transform(Vector3.UnitX, transform.Rotation);

        Vector3 start = transform.Position;
        Vector3 end = start + (direction * ArrowLength);

        _debugRenderer.AddLine(start, end, color);
        DrawArrowHead(end, direction, right, up, color);
        DrawCircle(start, right, up, CircleRadius, color);
    }

    private void DrawSpotlightGizmo(Light light)
    {
        if (light.GameObject is null)
        {
            return;
        }

        Transform transform = light.GameObject.Transform;
        Vector3 color = SpotlightColor;

        Vector3 origin = transform.Position;
        Vector3 direction = transform.Forward;
        Vector3 up = Vector3.Transform(Vector3.UnitY, transform.Rotation);
        Vector3 right = Vector3.Transform(Vector3.UnitX, transform.Rotation);

        float outerAngleRad = light.OuterConeAngle * float.Pi / 180.0f;
        float outerRadius = light.Range * float.Tan(outerAngleRad);
        Vector3 circleCenter = origin + (direction * light.Range);

        _debugRenderer.AddLine(origin, circleCenter + (right * outerRadius), color);
        _debugRenderer.AddLine(origin, circleCenter - (right * outerRadius), color);
        _debugRenderer.AddLine(origin, circleCenter + (up * outerRadius), color);
        _debugRenderer.AddLine(origin, circleCenter - (up * outerRadius), color);

        DrawCircle(circleCenter, right, up, outerRadius, color);
    }

    private void DrawArrowHead(Vector3 tip, Vector3 direction, Vector3 right, Vector3 up, Vector3 color)
    {
        Vector3 baseCenter = tip - (direction * ArrowHeadSize);

        _debugRenderer.AddLine(tip, baseCenter + (right * ArrowHeadSize), color);
        _debugRenderer.AddLine(tip, baseCenter - (right * ArrowHeadSize), color);
        _debugRenderer.AddLine(tip, baseCenter + (up * ArrowHeadSize), color);
        _debugRenderer.AddLine(tip, baseCenter - (up * ArrowHeadSize), color);
    }

    private void DrawCircle(Vector3 center, Vector3 right, Vector3 up, float radius, Vector3 color)
    {
        float step = 2.0f * float.Pi / CircleSegments;

        for (int i = 0; i < CircleSegments; i++)
        {
            float angle1 = i * step;
            float angle2 = (i + 1) * step;

            Vector3 offset1 = ((right * float.Cos(angle1)) + (up * float.Sin(angle1))) * radius;
            Vector3 offset2 = ((right * float.Cos(angle2)) + (up * float.Sin(angle2))) * radius;

            _debugRenderer.AddLine(center + offset1, center + offset2, color);
        }
    }

    private void DrawRigidBodyGizmo(GameObject gameObject)
    {
        RigidBody? rigidBody = gameObject.GetComponent<RigidBody>();

        if (rigidBody?.JitterBody is null)
        {
            return;
        }

        Vector3 color = GetRigidBodyGizmoColor(rigidBody);
        Vector3 position = rigidBody.JitterBody.Position.ToNumerics();
        Quaternion orientation = rigidBody.JitterBody.Orientation.ToNumerics();

        switch (rigidBody.JitterBody.Shape)
        {
            case BoxShape box:
                _debugRenderer.AddBox(position, orientation, box.Size.ToNumerics(), color);
                break;

            case SphereShape sphere:
                _debugRenderer.AddSphere(position, sphere.Radius, color);
                break;

            case CapsuleShape capsule:
                _debugRenderer.AddCapsule(position, orientation, capsule.Length, capsule.Radius, color);
                break;
        }
    }

    private static Vector3 GetRigidBodyGizmoColor(RigidBody rigidBody)
    {
        return rigidBody.JitterBody?.IsStatic == true
            ? StaticBodyColor
            : DynamicBodyColor;
    }
}