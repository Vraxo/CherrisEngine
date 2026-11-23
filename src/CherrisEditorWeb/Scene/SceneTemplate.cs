using Cherris.Components;
using Cherris.Core;
using Cherris.Serialization;
using System.Numerics;

namespace CherrisEditorWeb;

/// <summary>
/// A new scene, as a file: the objects it starts with, and the rules its name has to follow.
/// </summary>
/// <remarks>
/// <para>
/// A scene needs a camera to be looked through and a light to be lit, and nothing else: those two are what the
/// editor adds, so a new scene draws the moment it opens. The camera sits at the same default view the editor uses
/// when it has to add one to a scene that has none, so a new scene and a repaired one look alike.
/// </para>
/// <para>
/// The file is written by the engine's own <see cref="SceneSerializer"/>, not by a template string here, so what a
/// new scene contains is decided by the same code that saves scenes. Anything that could not be read back is
/// therefore something the engine could not save either — one rule, not two.
/// </para>
/// </remarks>
public static class SceneTemplate
{
    /// <summary>
    /// Characters a scene name may not contain. The list is the same on every platform on purpose: a project is
    /// usually committed and opened somewhere else, and a scene called <c>Level:1</c> would only reveal itself as a
    /// problem on the machine that cannot write it.
    /// </summary>
    private static readonly char[] InvalidNameChars = ['\\', '/', ':', '*', '?', '"', '<', '>', '|'];

    /// <summary>The extension scenes are saved with, which is what the content browser previews.</summary>
    public const string Extension = ".yaml";

    /// <summary>The name the New Scene dialog starts with.</summary>
    public const string DefaultName = "NewScene";

    /// <summary>The camera's default position, shared with the camera the editor adds to a scene without one.</summary>
    public static Vector3 DefaultCameraPosition { get; } = new(0f, 2.2f, 6.5f);

    /// <summary>The camera's default rotation, a few degrees down from level.</summary>
    public static Quaternion DefaultCameraRotation { get; } =
        Quaternion.CreateFromAxisAngle(Vector3.UnitX, -0.2f);

    /// <summary>
    /// The objects every new scene starts with: a camera at the default view, and a sun so the scene is lit from the
    /// moment it opens.
    /// </summary>
    public static List<GameObject> CreateObjects() => [CreateCamera(), CreateSun()];

    /// <summary>
    /// The camera a scene is looked through. Shared with the one the editor adds to a scene that has none, so a new
    /// scene and a repaired one start from the same view.
    /// </summary>
    public static GameObject CreateCamera()
    {
        GameObject camera = new("Camera");
        camera.Transform.Position = DefaultCameraPosition;
        camera.Transform.Rotation = DefaultCameraRotation;
        camera.AddComponent(new Camera());

        return camera;
    }

    /// <summary>The sun a new scene is lit by.</summary>
    public static GameObject CreateSun()
    {
        GameObject sun = new("Sun");
        sun.Transform.Rotation = Quaternion.CreateFromYawPitchRoll(0.9f, -0.8f, 0f);
        sun.AddComponent(new Light
        {
            Type = LightType.Directional,
            Intensity = 1.1f,
            AmbientStrength = 0.35f
        });

        return sun;
    }

    /// <summary>The file contents of a new scene, as the engine's serializer writes them.</summary>
    public static string CreateText() => new SceneSerializer().SerializeSceneToString(CreateObjects());

    /// <summary>
    /// Writes a new scene into <paramref name="directory"/> and returns its full path.
    /// </summary>
    /// <param name="directory">The folder to write into, normally the project root.</param>
    /// <param name="name">The name the user typed; see <see cref="TryNormalizeName"/>.</param>
    /// <exception cref="ArgumentException">The name is not usable as a scene file name.</exception>
    /// <exception cref="IOException">A file with that name is already there, or it could not be written.</exception>
    /// <exception cref="UnauthorizedAccessException">The folder cannot be written to.</exception>
    public static string Write(string directory, string name)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(directory);

        if (!TryNormalizeName(name, out string fileName, out string error))
        {
            throw new ArgumentException(error, nameof(name));
        }

        string path = Path.Combine(directory, fileName);

        // Checked up front so the message names the file rather than quoting the operating system's.
        if (File.Exists(path))
        {
            throw new IOException($"There is already a scene called {fileName} in this project.");
        }

        Directory.CreateDirectory(directory);

        // CreateNew rather than WriteAllText: a file that appeared since the check above must not be overwritten.
        using (FileStream stream = new(path, FileMode.CreateNew, FileAccess.Write, FileShare.None))
        using (StreamWriter writer = new(stream))
        {
            writer.Write(CreateText());
        }

        return path;
    }

    /// <summary>
    /// Checks a name for a new scene and normalises it into a file name, adding <see cref="Extension"/> when the
    /// user left it off.
    /// </summary>
    /// <param name="name">What the user typed.</param>
    /// <param name="fileName">The file name to write, when this returns true.</param>
    /// <param name="error">Why the name cannot be used, when this returns false.</param>
    public static bool TryNormalizeName(string? name, out string fileName, out string error)
    {
        fileName = string.Empty;
        error = string.Empty;

        string trimmed = (name ?? string.Empty).Trim();

        if (trimmed.Length == 0)
        {
            error = "Give the scene a name.";
            return false;
        }

        // A scene lives in the project folder, so a name may not point anywhere else, hidden or otherwise, and may
        // not hold anything a file name cannot say on every platform the project may be opened on.
        if (trimmed.StartsWith('.') || trimmed.IndexOfAny(InvalidNameChars) >= 0 || trimmed.Any(char.IsControl))
        {
            error = "A scene name cannot contain a path or any of \\ / : * ? \" < > |.";
            return false;
        }

        if (!trimmed.EndsWith(Extension, StringComparison.OrdinalIgnoreCase) &&
            !trimmed.EndsWith(".yml", StringComparison.OrdinalIgnoreCase))
        {
            trimmed += Extension;
        }

        fileName = trimmed;

        return true;
    }
}
