using Cherris.Components;
using Cherris.Core;
using Jitter.Collision.Shapes;
using System.Numerics;

namespace Cherris.Rendering.OpenTK;

internal class OpenGLGizmoRenderer
{
    private readonly OpenGLDebugRenderer _debugRenderer;

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

        if (showPhysicsColliders)
        {
            foreach (var go in gameObjects)
            {
                DrawRigidBodyGizmo(go);
            }
        }
    }

    private void DrawLightGizmo(Light light)
    {
        if (light.Type == LightType.Directional)
        {
            DrawDirectionalLightGizmo(light);
        }
        else if (light.Type == LightType.Spot)
        {
            DrawSpotlightGizmo(light);
        }
    }

    private void DrawDirectionalLightGizmo(Light light)
    {
        var transform = light.GameObject.Transform;
        var color = new Vector3(1.0f, 0.9f, 0.2f); // Yellow
        float gizmoSize = 2.0f;
        float arrowHeadSize = 0.25f;
        float arrowLength = 1.0f * gizmoSize;

        var direction = Vector3.Transform(-Vector3.UnitZ, transform.Rotation);
        var up = Vector3.Transform(Vector3.UnitY, transform.Rotation);
        var right = Vector3.Transform(Vector3.UnitX, transform.Rotation);

        var start = transform.Position;
        var end = start + (direction * arrowLength);

        _debugRenderer.AddLine(start, end, color);

        // Arrow head
        _debugRenderer.AddLine(end, end - (direction * arrowHeadSize) + (right * arrowHeadSize), color);
        _debugRenderer.AddLine(end, end - (direction * arrowHeadSize) - (right * arrowHeadSize), color);
        _debugRenderer.AddLine(end, end - (direction * arrowHeadSize) + (up * arrowHeadSize), color);
        _debugRenderer.AddLine(end, end - (direction * arrowHeadSize) - (up * arrowHeadSize), color);

        // Ring
        const int circleSegments = 16;
        float circleRadius = 0.5f * gizmoSize;
        for (int i = 0; i < circleSegments; i++)
        {
            float angle1 = i / (float)circleSegments * 2.0f * MathF.PI;
            float angle2 = (i + 1) / (float)circleSegments * 2.0f * MathF.PI;

            var p1 = transform.Position + (((right * MathF.Cos(angle1)) + (up * MathF.Sin(angle1))) * circleRadius);
            var p2 = transform.Position + (((right * MathF.Cos(angle2)) + (up * MathF.Sin(angle2))) * circleRadius);
            _debugRenderer.AddLine(p1, p2, color);
        }
    }

    private void DrawSpotlightGizmo(Light light)
    {
        var transform = light.GameObject.Transform;
        var color = new Vector3(1.0f, 0.9f, 0.2f); // Yellow

        var origin = transform.Position;
        var direction = transform.Forward;
        var up = Vector3.Transform(Vector3.UnitY, transform.Rotation);
        var right = Vector3.Transform(Vector3.UnitX, transform.Rotation);

        float range = light.Range;
        float outerAngleRad = light.OuterConeAngle * MathF.PI / 180.0f;
        float outerRadius = range * MathF.Tan(outerAngleRad);

        var circleCenter = origin + (direction * range);

        // Cone edges
        _debugRenderer.AddLine(origin, circleCenter + (right * outerRadius), color);
        _debugRenderer.AddLine(origin, circleCenter - (right * outerRadius), color);
        _debugRenderer.AddLine(origin, circleCenter + (up * outerRadius), color);
        _debugRenderer.AddLine(origin, circleCenter - (up * outerRadius), color);

        // Base circle
        const int circleSegments = 16;
        for (int i = 0; i < circleSegments; i++)
        {
            float angle1 = i / (float)circleSegments * 2.0f * MathF.PI;
            float angle2 = (i + 1) / (float)circleSegments * 2.0f * MathF.PI;

            var p1 = circleCenter + (((right * MathF.Cos(angle1)) + (up * MathF.Sin(angle1))) * outerRadius);
            var p2 = circleCenter + (((right * MathF.Cos(angle2)) + (up * MathF.Sin(angle2))) * outerRadius);
            _debugRenderer.AddLine(p1, p2, color);
        }
    }

    private void DrawRigidBodyGizmo(GameObject go)
    {
        var rb = go.GetComponent<RigidBody>();
        if (rb?.JitterBody is null)
        {
            return;
        }

        // Green for static, purple for dynamic
        var color = rb.JitterBody.IsStatic
            ? new Vector3(0.2f, 0.8f, 0.2f)
            : new Vector3(0.8f, 0.2f, 0.8f);

        var position = rb.JitterBody.Position.ToNumerics();
        var orientation = rb.JitterBody.Orientation.ToNumerics();

        switch (rb.JitterBody.Shape)
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
}