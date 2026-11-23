using Cherris;
using Cherris.Components;
using Cherris.Core;
using System.Numerics;

namespace CherrisEditorWeb;

/// <summary>
/// Turns a click in the viewport into the object under it.
/// </summary>
/// <remarks>
/// <para>
/// The same rules the ImGui editor's viewport selection follows: a ray from the main camera through the pixel,
/// tested against each object's world-space bounds, nearest hit wins. Objects that are not things you would click
/// on — the skybox, and cameras — are skipped.
/// </para>
/// <para>
/// Pixels here are viewport pixels: the same space the frame is rendered in, which is the space the browser
/// reports the pointer in (device pixels relative to the canvas), so nothing has to be scaled.
/// </para>
/// <para>
/// Bounds are boxes, not the meshes inside them, so the corner of a rotated object picks as if it filled its
/// axis-aligned box. That is what the ImGui editor does too, and it is what makes picking cheap enough to run on
/// a click.
/// </para>
/// </remarks>
public static class ViewportPicker
{
    /// <summary>
    /// The object under <paramref name="pointer"/>, or null when the click hit nothing — which is also what a
    /// missing camera, a zero-sized viewport and a click outside the viewport return.
    /// </summary>
    /// <param name="scene">The scene to test; null picks nothing.</param>
    /// <param name="pointer">Pointer position in viewport pixels, measured from the viewport's top-left.</param>
    /// <param name="viewport">Viewport size in pixels.</param>
    public static GameObject? Pick(Scene? scene, Vector2 pointer, Vector2 viewport)
    {
        if (scene?.MainCamera is not { } camera || !TryCreateRay(camera, pointer, viewport, out Ray ray))
        {
            return null;
        }

        GameObject? closest = null;
        float closestDistance = float.MaxValue;

        foreach (GameObject gameObject in scene.GameObjects)
        {
            if (!IsPickable(gameObject))
            {
                continue;
            }

            if (!ray.Intersects(gameObject.GetWorldSpaceAABB(), out float distance))
            {
                continue;
            }

            if (distance >= closestDistance)
            {
                continue;
            }

            closestDistance = distance;
            closest = gameObject;
        }

        return closest;
    }

    /// <summary>
    /// Builds the ray a click at <paramref name="pointer"/> casts into the scene, from the camera's position along
    /// the direction that pixel looks in.
    /// </summary>
    /// <returns>False when there is no usable ray: a detached camera, an empty viewport, or a click outside it.</returns>
    public static bool TryCreateRay(Camera camera, Vector2 pointer, Vector2 viewport, out Ray ray)
    {
        ArgumentNullException.ThrowIfNull(camera);

        ray = default;

        if (viewport.X <= 0f || viewport.Y <= 0f)
        {
            return false;
        }

        if (pointer.X < 0f || pointer.Y < 0f || pointer.X > viewport.X || pointer.Y > viewport.Y)
        {
            return false;
        }

        if (camera.GameObject is not { } owner)
        {
            return false;
        }

        Matrix4x4 projection = camera.GetProjectionMatrix(viewport.X / viewport.Y);

        if (!Matrix4x4.Invert(projection, out Matrix4x4 inverseProjection) ||
            !Matrix4x4.Invert(camera.GetViewMatrix(), out Matrix4x4 inverseView))
        {
            return false;
        }

        // Clip space runs -1..1 across the viewport with +Y up, so screen Y is flipped on the way in.
        Vector4 clip = new(
            (2f * pointer.X / viewport.X) - 1f,
            1f - (2f * pointer.Y / viewport.Y),
            1f,
            1f);

        Vector4 eye = Vector4.Transform(clip, inverseProjection);
        eye.Z = -1f;
        eye.W = 0f;

        Vector4 world = Vector4.Transform(eye, inverseView);
        Vector3 direction = new(world.X, world.Y, world.Z);

        if (direction.LengthSquared() < 1e-8f)
        {
            return false;
        }

        ray = new Ray(owner.Transform.Position, Vector3.Normalize(direction));

        return true;
    }

    /// <summary>What a click can land on: not the skybox, and not a camera.</summary>
    private static bool IsPickable(GameObject gameObject) =>
        gameObject.GetComponent<Skybox>() is null && gameObject.GetComponent<Camera>() is null;
}
