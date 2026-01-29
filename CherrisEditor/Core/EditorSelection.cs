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
        Ray ray = CreateRayFromViewport(mousePos, viewportPos, viewportSize);
        GameObject? closestObject = null;
        float closestDistance = float.MaxValue;

        foreach (GameObject gameObject in _sceneManager.GameObjects)
        {
            if (gameObject.GetComponent<Skybox>() is not null || gameObject.GetComponent<Camera>() is not null)
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

    public Ray CreateRayFromViewport(Vector2 mousePos, Vector2 viewportPos, Vector2 viewportSize)
    {
        Camera? camera = _sceneManager.MainCamera;
        if (camera is null || viewportSize.X <= 0 || viewportSize.Y <= 0)
        {
            return new Ray(new Vector3(float.MaxValue), Vector3.Zero);
        }

        Vector2 relativeMouse = mousePos - viewportPos;

        bool isOutsideViewport = relativeMouse.X < 0
            || relativeMouse.Y < 0
            || relativeMouse.X > viewportSize.X
            || relativeMouse.Y > viewportSize.Y;

        return isOutsideViewport
            ? new Ray(new Vector3(float.MaxValue), Vector3.Zero)
            : ViewportToWorldRay(relativeMouse, viewportSize, camera);
    }

    private Ray ViewportToWorldRay(Vector2 relativeMouse, Vector2 viewportSize, Camera camera)
    {
        float normalizedX = (2.0f * relativeMouse.X / viewportSize.X) - 1.0f;
        float normalizedY = 1.0f - (2.0f * relativeMouse.Y / viewportSize.Y);
        Vector4 clipSpaceRay = new(normalizedX, normalizedY, 1.0f, 1.0f);

        Matrix4x4 projectionMatrix = camera.GetProjectionMatrix(viewportSize.X / viewportSize.Y);
        Matrix4x4.Invert(projectionMatrix, out Matrix4x4 inverseProjection);
        Vector4 viewSpaceRay = Vector4.Transform(clipSpaceRay, inverseProjection);
        viewSpaceRay.Z = -1.0f;
        viewSpaceRay.W = 0.0f;

        Matrix4x4 viewMatrix = camera.GetViewMatrix();
        Matrix4x4.Invert(viewMatrix, out Matrix4x4 inverseView);
        Vector4 worldSpaceRay = Vector4.Transform(viewSpaceRay, inverseView);

        Vector3 rayDirection = Vector3.Normalize(new Vector3(worldSpaceRay.X, worldSpaceRay.Y, worldSpaceRay.Z));
        Vector3 rayOrigin = camera.GameObject.Transform.Position;

        return new Ray(rayOrigin, rayDirection);
    }
}