using System.Globalization;
using System.Numerics;
using YamlDotNet.Core;
using YamlDotNet.Serialization;

namespace CherrisEditorSession;

/// <summary>What a scene preview draws an object as.</summary>
public enum ThumbnailObjectKind
{
    /// <summary>Anything with a <c>MeshRenderer</c>: drawn as its footprint.</summary>
    Mesh,

    /// <summary>Anything with a <c>Light</c>: drawn as a dot, since a light has no footprint.</summary>
    Light,

    /// <summary>A <c>Camera</c>: drawn as a marker pointing where it looks.</summary>
    Camera,

    /// <summary>Anything else (scripts only): drawn as a small outline so the object is still visible.</summary>
    Other
}

/// <summary>
/// One object of a scene, flattened onto the ground plane. This is all a preview needs: where the object is,
/// how wide it is on the floor, and which way it faces.
/// </summary>
/// <param name="Name">The object's name, for tooltips.</param>
/// <param name="Kind">What to draw it as.</param>
/// <param name="X">World X of the object's origin.</param>
/// <param name="Z">World Z of the object's origin.</param>
/// <param name="Width">Footprint along X, in world units.</param>
/// <param name="Depth">Footprint along Z, in world units.</param>
/// <param name="YawRadians">Heading around Y, from the scene's Euler rotation.</param>
public sealed record ThumbnailObject(
    string Name,
    ThumbnailObjectKind Kind,
    float X,
    float Z,
    float Width,
    float Depth,
    float YawRadians);

/// <summary>
/// A scene reduced to what a thumbnail can draw, read straight out of the scene file's text.
/// </summary>
/// <remarks>
/// <para>
/// Nothing is loaded and nothing is constructed: no components, no resources, no GL context. A preview has to be
/// available for every scene in a folder, including the ones that are not open, so it comes from the YAML the
/// serializer already writes — names, the transform, and which component types each object carries.
/// </para>
/// <para>
/// A mesh is drawn at the size its scale describes, because the preview cannot know how big a named mesh really
/// is without loading it. A unit cube at scale 1.5 is a 1.5×1.5 footprint; the demo's floor, a plane at scale 1,
/// comes out as a 1×1 tile rather than a 20-unit floor. It is a map of the scene, not a render of it.
/// </para>
/// </remarks>
public sealed class SceneThumbnail
{
    /// <summary>The reader is stateless and thread-safe; one instance is enough for every file read.</summary>
    private static readonly IDeserializer Reader = new DeserializerBuilder().Build();

    private SceneThumbnail(IReadOnlyList<ThumbnailObject> objects) => Objects = objects;

    /// <summary>A scene with no objects in it, which is what a new project's start scene is.</summary>
    public static SceneThumbnail Empty { get; } = new([]);

    /// <summary>Every object of the scene, in file order.</summary>
    public IReadOnlyList<ThumbnailObject> Objects { get; }

    /// <summary>True when the scene has no objects at all.</summary>
    public bool IsEmpty => Objects.Count == 0;

    /// <summary>
    /// Reads a scene file's text, or returns null when the text is not a scene. A file without a
    /// <c>GameObjects</c> root is not one — <c>project.yaml</c> is the obvious example — and the content browser
    /// keeps its ordinary icon for those.
    /// </summary>
    public static SceneThumbnail? Parse(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return null;
        }

        object? root;

        try
        {
            root = Reader.Deserialize<object?>(text);
        }
        catch (YamlException)
        {
            // A half-written or hand-edited file is not a scene the browser can draw; the icon stays generic.
            return null;
        }
        catch (Exception exception) when (exception is InvalidOperationException or FormatException)
        {
            return null;
        }

        if (root is not IDictionary<object, object> scene || !scene.TryGetValue("GameObjects", out object? node))
        {
            return null;
        }

        List<ThumbnailObject> objects = [];

        if (node is List<object> entries)
        {
            foreach (object? entry in entries)
            {
                if (entry is IDictionary<object, object> gameObject)
                {
                    objects.Add(ReadObject(gameObject));
                }
            }
        }

        return new SceneThumbnail(objects);
    }

    private static ThumbnailObject ReadObject(IDictionary<object, object> gameObject)
    {
        string name = ReadScalar(gameObject, "Name") ?? "GameObject";

        IDictionary<object, object>? components = ReadMap(gameObject, "Components");
        IDictionary<object, object>? transform = components is null ? null : ReadMap(components, "Transform");

        Vector3 position = ReadVector(transform, "Position");
        Vector3 scale = ReadVector(transform, "Scale", Vector3.One);
        Vector3 rotation = ReadVector(transform, "Rotation");

        ThumbnailObjectKind kind =
            components?.ContainsKey("Camera") == true ? ThumbnailObjectKind.Camera
            : components?.ContainsKey("Light") == true ? ThumbnailObjectKind.Light
            : components?.ContainsKey("MeshRenderer") == true ? ThumbnailObjectKind.Mesh
            : ThumbnailObjectKind.Other;

        // A mesh covers the ground its scale describes; everything else is a marker, so its size is not used.
        float width = kind == ThumbnailObjectKind.Mesh ? MathF.Abs(scale.X) : 0f;
        float depth = kind == ThumbnailObjectKind.Mesh ? MathF.Abs(scale.Z) : 0f;

        // Only the heading matters for a marker; the serializer writes Euler degrees.
        float yaw = rotation.Y * (MathF.PI / 180f);

        return new ThumbnailObject(name, kind, position.X, position.Z, width, depth, yaw);
    }

    private static IDictionary<object, object>? ReadMap(IDictionary<object, object> map, string key) =>
        map.TryGetValue(key, out object? value) ? value as IDictionary<object, object> : null;

    private static string? ReadScalar(IDictionary<object, object> map, string key) =>
        map.TryGetValue(key, out object? value) ? value as string : null;

    private static Vector3 ReadVector(IDictionary<object, object>? map, string key, Vector3 fallback = default)
    {
        if (map is null || !map.TryGetValue(key, out object? value) || value is not List<object> parts || parts.Count < 2)
        {
            return fallback;
        }

        float x = Number(parts, 0, fallback.X);
        float y = Number(parts, 1, fallback.Y);
        float z = Number(parts, 2, fallback.Z);

        return new Vector3(x, y, z);
    }

    private static float Number(List<object> parts, int index, float fallback)
    {
        if (index >= parts.Count)
        {
            return fallback;
        }

        string? text = Convert.ToString(parts[index], CultureInfo.InvariantCulture);

        return float.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out float number)
            ? number
            : fallback;
    }
}
