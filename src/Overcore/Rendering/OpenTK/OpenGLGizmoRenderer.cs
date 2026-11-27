using Overcore.Components;
using Overcore.Core;
using Overcore.Navigation;
using System.Numerics;

namespace Overcore.Rendering.OpenTK;

internal class OpenGLGizmoRenderer
{
    private readonly OpenGLDebugRenderer _debugRenderer;

    private static readonly Vector3 DirectionalLightColor = new(1.0f, 0.9f, 0.2f);
    private static readonly Vector3 SpotlightColor = new(1.0f, 0.9f, 0.2f);
    private static readonly Vector3 StaticBodyColor = new(0.2f, 0.8f, 0.2f);
    private static readonly Vector3 DynamicBodyColor = new(0.8f, 0.2f, 0.8f);
    private static readonly Vector3 ParticleShapeColor = new(0.35f, 0.85f, 1.0f);
    private static readonly Vector3 NavMeshEdgeColor = new(0.15f, 0.75f, 0.95f);
    private static readonly Vector3 NavMeshBoundaryColor = new(0.05f, 0.45f, 0.85f);
    private static readonly Vector3 NavAgentColor = new(0.95f, 0.65f, 0.15f);
    private static readonly Vector3 NavPathColor = new(1.0f, 0.25f, 0.25f);
    private static readonly Vector3 NavObstacleColor = new(0.95f, 0.35f, 0.2f);

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

        if (selectedObject?.GetComponent<ParticleSystem>() is ParticleSystem particleSystem)
        {
            DrawParticleSystemGizmo(particleSystem);
        }

        if (selectedObject?.GetComponent<NavMeshSurface>() is NavMeshSurface selectedSurface)
        {
            DrawNavMeshSurfaceGizmo(selectedSurface);
        }

        if (selectedObject?.GetComponent<NavMeshAgent>() is NavMeshAgent selectedAgent)
        {
            DrawNavMeshAgentGizmo(selectedAgent);
        }

        if (selectedObject?.GetComponent<NavMeshObstacle>() is NavMeshObstacle selectedObstacle)
        {
            DrawNavMeshObstacleGizmo(selectedObstacle);
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

        if (rigidBody?.BepuBodyHandle is null && rigidBody?.BepuStaticHandle is null)
        {
            return;
        }

        Vector3 color = GetRigidBodyGizmoColor(rigidBody);
        Vector3 position = gameObject.Transform.Position;
        Quaternion orientation = gameObject.Transform.Rotation;

        // Get size from mesh renderer
        var meshRenderer = gameObject.GetComponent<MeshRenderer>();
        if (meshRenderer?.Mesh is null)
        {
            return;
        }

        BoundingBox aabb = meshRenderer.Mesh.AABB;
        Vector3 size = (aabb.Max - aabb.Min) * gameObject.Transform.Scale;

        switch (rigidBody.Shape)
        {
            case ColliderType.Box:
                _debugRenderer.AddBox(position, orientation, size, color);
                break;

            case ColliderType.Sphere:
                float radius = (size.X + size.Y + size.Z) / 6.0f;
                _debugRenderer.AddSphere(position, radius, color);
                break;

            case ColliderType.Capsule:
                float capsuleRadius = Math.Max(size.X, size.Z) / 2.0f;
                float capsuleLength = Math.Max(0, size.Y - (2 * capsuleRadius));
                _debugRenderer.AddCapsule(position, orientation, capsuleLength, capsuleRadius, color);
                break;
        }
    }

    private static Vector3 GetRigidBodyGizmoColor(RigidBody rigidBody)
    {
        return rigidBody.IsStatic
            ? StaticBodyColor
            : DynamicBodyColor;
    }

    private void DrawParticleSystemGizmo(ParticleSystem ps)
    {
        if (ps.GameObject is null || !ps.ShapeEnabled)
        {
            return;
        }

        ps.Transform.GetWorldTransform(out Vector3 origin, out Quaternion rot, out Vector3 scale);
        Vector3 right = Vector3.Transform(Vector3.UnitX, rot);
        Vector3 up = Vector3.Transform(Vector3.UnitY, rot);
        Vector3 forward = Vector3.Transform(Vector3.UnitZ, rot);
        float maxHorizontalScale = MathF.Max(MathF.Abs(scale.X), MathF.Abs(scale.Z));
        float r = MathF.Max(0.01f, ps.ShapeRadius * maxHorizontalScale);

        switch (ps.ShapeType)
        {
            case ParticleShapeType.Box:
                _debugRenderer.AddBox(origin, rot, Vector3.Abs(ps.BoxScale * scale), ParticleShapeColor);
                break;

            case ParticleShapeType.Sphere:
                _debugRenderer.AddSphere(origin, r, ParticleShapeColor);
                break;

            case ParticleShapeType.Hemisphere:
            case ParticleShapeType.Circle:
                DrawCircle(origin, right, forward, r, ParticleShapeColor);
                break;

            case ParticleShapeType.Cone:
            default:
            {
                DrawCircle(origin, right, forward, r, ParticleShapeColor);
                float coneHeight = MathF.Max(1.0f, ps.StartSpeed * 0.35f);
                float topRadius = r + (coneHeight * MathF.Tan(Math.Clamp(ps.ConeAngle, 0f, 85f) * (MathF.PI / 180f)));
                Vector3 topCenter = origin + (up * coneHeight);
                DrawCircle(topCenter, right, forward, topRadius, ParticleShapeColor);
                _debugRenderer.AddLine(origin + (right * r), topCenter + (right * topRadius), ParticleShapeColor);
                _debugRenderer.AddLine(origin - (right * r), topCenter - (right * topRadius), ParticleShapeColor);
                _debugRenderer.AddLine(origin + (forward * r), topCenter + (forward * topRadius), ParticleShapeColor);
                _debugRenderer.AddLine(origin - (forward * r), topCenter - (forward * topRadius), ParticleShapeColor);
                break;
            }
        }
    }

    private void DrawNavMeshSurfaceGizmo(NavMeshSurface surface)
    {
        NavMeshData? data = surface.NavMeshData ?? NavMesh.ActiveData;
        if (data is null || data.IsEmpty)
        {
            return;
        }

        Vector3 lift = new(0f, 0.03f, 0f);
        Vector3[] verts = data.Vertices;
        for (int polyIndex = 0; polyIndex < data.Polygons.Count; polyIndex++)
        {
            NavMeshPolygon poly = data.Polygons[polyIndex];
            int n = poly.VertexIndices.Length;
            for (int e = 0; e < n; e++)
            {
                int neighbor = poly.Neighbors[e];
                // Draw boundary edges once, and shared interior portal edges once (from lower index to higher index).
                if (neighbor >= 0 && neighbor < polyIndex)
                {
                    continue;
                }

                Vector3 a = verts[poly.VertexIndices[e]] + lift;
                Vector3 b = verts[poly.VertexIndices[(e + 1) % n]] + lift;
                _debugRenderer.AddLine(a, b, neighbor < 0 ? NavMeshBoundaryColor : NavMeshEdgeColor);
            }
        }
    }

    private void DrawNavMeshAgentGizmo(NavMeshAgent agent)
    {
        if (agent.GameObject is null)
        {
            return;
        }

        Vector3 basePos = agent.Transform.WorldPosition;
        float radius = MathF.Max(0.05f, agent.Radius);
        float height = MathF.Max(radius * 2f, agent.Height);
        Vector3 capsuleCenter = basePos + new Vector3(0f, height * 0.5f, 0f);
        float cylinderLen = MathF.Max(0f, height - (radius * 2f));

        _debugRenderer.AddCapsule(capsuleCenter, Quaternion.Identity, cylinderLen, radius, NavAgentColor);

        IReadOnlyList<Vector3> corners = agent.Path.CornerList;
        Vector3 lift = new(0f, 0.06f, 0f);
        for (int i = 0; i < corners.Count - 1; i++)
        {
            _debugRenderer.AddLine(corners[i] + lift, corners[i + 1] + lift, NavPathColor);
        }
    }

    private void DrawNavMeshObstacleGizmo(NavMeshObstacle obstacle)
    {
        if (obstacle.GameObject is null)
        {
            return;
        }

        NavMeshCarveVolume vol = obstacle.GetWorldCarveVolume();
        if (vol.IsCapsule)
        {
            float cyl = MathF.Max(0f, vol.Height - (vol.Radius * 2f));
            _debugRenderer.AddCapsule(vol.Center, vol.Rotation, cyl, vol.Radius, NavObstacleColor);
        }
        else
        {
            _debugRenderer.AddBox(vol.Center, vol.Rotation, vol.HalfExtents * 2f, NavObstacleColor);
        }
    }
}