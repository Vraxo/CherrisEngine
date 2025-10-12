using System.Collections.Generic;

namespace Cherris;

public static class OutlineSystem
{
    private static readonly HashSet<GameObject> _outlinedObjects = new();

    public static IReadOnlyCollection<GameObject> OutlinedObjects => _outlinedObjects;

    public static void Register(GameObject gameObject)
    {
        _outlinedObjects.Add(gameObject);
    }

    public static void Unregister(GameObject gameObject)
    {
        _outlinedObjects.Remove(gameObject);
    }
}