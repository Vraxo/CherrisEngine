using System.Collections.Generic;
using System.Numerics;

// These classes are dumb data containers that perfectly match the YAML structure.
// YamlDotNet deserializes the file into these, and then our SceneLoader
// uses this information to build the actual GameObjects.

namespace VeldridCube
{
    public class SceneData
    {
        public List<GameObjectData> GameObjects { get; set; }
    }

    public class GameObjectData
    {
        public string Name { get; set; }
        public TransformData Transform { get; set; }
        public List<ComponentData> Components { get; set; }
    }

    public class TransformData
    {
        public Vector3 Position { get; set; }
        public Vector3 Rotation { get; set; } // Stored as Euler angles in YAML
        public Vector3 Scale { get; set; }
    }

    public class ComponentData
    {
        public string Type { get; set; }
        public Dictionary<string, string> Properties { get; set; }
    }
}