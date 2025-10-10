using System.Collections.Generic;
using System.Numerics;
using YamlDotNet.Serialization;

// These classes are dumb data containers that perfectly match the YAML structure.
// YamlDotNet deserializes the file into these, and then our SceneLoader
// uses this information to build the actual GameObjects.

namespace Cherris;

public class SceneData
{
    public List<GameObjectData> GameObjects { get; set; }
}

public class GameObjectData
{
    public string Name { get; set; }
    // This is now a map of component names to their properties.
    public Dictionary<string, object> Components { get; set; }
}

public class TransformData
{
    public Vector3 Position { get; set; } = Vector3.Zero;
    public Vector3 Rotation { get; set; } = Vector3.Zero; // Stored as Euler angles in YAML
    public Vector3 Scale { get; set; } = Vector3.One;
}