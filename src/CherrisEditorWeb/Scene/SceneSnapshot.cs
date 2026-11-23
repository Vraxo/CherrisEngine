using Cherris;
using Cherris.Components;
using Cherris.Core;
using System.Numerics;

namespace CherrisEditorWeb;

/// <summary>One object in a <see cref="SceneSnapshot"/>.</summary>
/// <param name="Id">Stable object identity, for selection and diffing.</param>
/// <param name="Name">Object name as shown in the hierarchy.</param>
/// <param name="Position">World position.</param>
/// <param name="Rotation">World rotation.</param>
/// <param name="Scale">Local scale.</param>
/// <param name="Components">Component type names attached to the object.</param>
/// <param name="ParentId">Parent object, or null for a root; used to rebuild the hierarchy tree.</param>
public sealed record SceneNodeSnapshot(
    Guid Id,
    string Name,
    Vector3 Position,
    Quaternion Rotation,
    Vector3 Scale,
    IReadOnlyList<string> Components,
    Guid? ParentId = null)
{
    /// <summary>
    /// <see cref="Rotation"/> as Euler degrees, X pitch, Y yaw, Z roll — the reading the inspector shows,
    /// because a quaternion's four components are not what an editor user types.
    /// </summary>
    /// <remarks>
    /// Same conversion as the ImGui inspector (<c>TransformInspector</c>), which writes the angles back with
    /// <see cref="Quaternion.CreateFromYawPitchRoll"/>.
    /// </remarks>
    public Vector3 RotationDegrees => EngineMath.ToEulerAngles(Rotation) * (180f / MathF.PI);
}

/// <summary>
/// Immutable view of the active scene, safe to read from a UI thread while the render thread keeps
/// mutating the live scene.
/// </summary>
/// <param name="SceneName">Active scene name, or null when no scene is open.</param>
/// <param name="Nodes">Objects in the scene, in hierarchy order.</param>
/// <param name="Frame">Engine frame number the snapshot was taken on.</param>
/// <param name="CapturedAt">Wall-clock time the snapshot was taken.</param>
public sealed record SceneSnapshot(
    string? SceneName,
    IReadOnlyList<SceneNodeSnapshot> Nodes,
    long Frame,
    DateTimeOffset CapturedAt);

/// <summary>Builds <see cref="SceneSnapshot"/> instances from a live scene.</summary>
public static class SceneSnapshotBuilder
{
    /// <summary>
    /// Copies the scene's objects into an immutable snapshot. Call from the thread that owns the scene.
    /// </summary>
    public static SceneSnapshot Build(Scene? scene, long frame, DateTimeOffset capturedAt)
    {
        if (scene is null)
        {
            return new SceneSnapshot(null, [], frame, capturedAt);
        }

        List<SceneNodeSnapshot> nodes = new(scene.GameObjects.Count);

        foreach (GameObject gameObject in scene.GameObjects)
        {
            List<string> components = [];

            foreach (Component component in gameObject.Components)
            {
                components.Add(component.GetType().Name);
            }

            nodes.Add(new SceneNodeSnapshot(
                gameObject.Id,
                gameObject.Name,
                gameObject.Transform.Position,
                gameObject.Transform.Rotation,
                gameObject.Transform.Scale,
                components,
                gameObject.Transform.Parent?.GameObject.Id));
        }

        return new SceneSnapshot(scene.Name, nodes, frame, capturedAt);
    }
}
