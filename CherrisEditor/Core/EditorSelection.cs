using Cherris;
using Cherris.Components;
using Cherris.Core;
using System.Numerics;

namespace CherrisEditor.Core;

public class EditorSelection
{
    private readonly SceneManager _sceneManager;

    public EditorSelection(SceneManager sceneManager)
    {
        _sceneManager = sceneManager;
    }

    public GameObject? PickObject(Vector2 mousePos, Vector2 viewportPos, Vector2 viewportSize)
    {
        Ray? ray = CreateRayFromViewport(mousePos, viewportPos, viewportSize);

        return ray is null ? null : FindClosestIntersection(ray.Value);
    }

    private GameObject? FindClosestIntersection(Ray ray)
    {
        GameObject? closestObject = null;
        float closestDistance = float.MaxValue;

        foreach (GameObject gameObject in _sceneManager.GameObjects)
        {
            if (!IsSelectable(gameObject))
            {
                continue;
            }

            BoundingBox aabb = gameObject.GetWorldSpaceAABB();

            if (!ray.Intersects(aabb, out float distance))
            {
                continue;
            }

            if (distance >= closestDistance)
            {
                continue;
            }

            closestDistance = distance;
            closestObject = gameObject;
        }

        return closestObject;
    }

    private static bool IsSelectable(GameObject gameObject)
    {
        return gameObject.GetComponent<Skybox>() is null
            && gameObject.GetComponent<Camera>() is null;
    }

    private Ray? CreateRayFromViewport(Vector2 mousePos, Vector2 viewportPos, Vector2 viewportSize)
    {
        if (_sceneManager.MainCamera is not Camera camera)
        {
            return null;
        }

        if (viewportSize.X <= 0 || viewportSize.Y <= 0)
        {
            return null;
        }

        Vector2 relativeMouse = mousePos - viewportPos;

        return IsOutsideViewport(relativeMouse, viewportSize) ? null : BuildViewportRay(relativeMouse, viewportSize, camera);
    }

    private static bool IsOutsideViewport(Vector2 relativeMouse, Vector2 viewportSize)
    {
        return relativeMouse.X < 0
            || relativeMouse.Y < 0
            || relativeMouse.X > viewportSize.X
            || relativeMouse.Y > viewportSize.Y;
    }

    private static Ray? BuildViewportRay(Vector2 relativeMouse, Vector2 viewportSize, Camera camera)
    {
        Vector4 clipSpaceRay = CalculateClipSpaceRay(relativeMouse, viewportSize);
        Vector3? worldDirection = TransformToWorldDirection(clipSpaceRay, viewportSize, camera);

        return worldDirection is null || camera.GameObject is not GameObject gameObject
            ? null
            : (Ray?)new Ray(gameObject.Transform.Position, worldDirection.Value);
    }

    private static Vector4 CalculateClipSpaceRay(Vector2 relativeMouse, Vector2 viewportSize)
    {
        float normalizedX = (2.0f * relativeMouse.X / viewportSize.X) - 1.0f;
        float normalizedY = 1.0f - (2.0f * relativeMouse.Y / viewportSize.Y);

        return new Vector4(normalizedX, normalizedY, 1.0f, 1.0f);
    }

    private static Vector3? TransformToWorldDirection(Vector4 clipSpaceRay, Vector2 viewportSize, Camera camera)
    {
        Matrix4x4 projectionMatrix = camera.GetProjectionMatrix(viewportSize.X / viewportSize.Y);

        if (!Matrix4x4.Invert(projectionMatrix, out Matrix4x4 inverseProjection))
        {
            return null;
        }

        Vector4 viewSpaceRay = Vector4.Transform(clipSpaceRay, inverseProjection);
        viewSpaceRay.Z = -1.0f;
        viewSpaceRay.W = 0.0f;

        Matrix4x4 viewMatrix = camera.GetViewMatrix();

        if (!Matrix4x4.Invert(viewMatrix, out Matrix4x4 inverseView))
        {
            return null;
        }

        Vector4 worldSpaceRay = Vector4.Transform(viewSpaceRay, inverseView);

        return Vector3.Normalize(new(worldSpaceRay.X, worldSpaceRay.Y, worldSpaceRay.Z));
    }
}