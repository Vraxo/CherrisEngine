using System.Collections.Generic;

namespace Cherris;

public static class OutlineSystem
{
    private static readonly Dictionary<string, HashSet<GameObject>> _objectsByProfile = new();

    public static IReadOnlyDictionary<string, HashSet<GameObject>> ObjectsByProfile => _objectsByProfile;

    public static void Register(GameObject gameObject, string profileName)
    {
        if (string.IsNullOrEmpty(profileName)) return;

        if (!_objectsByProfile.TryGetValue(profileName, out var objectSet))
        {
            objectSet = new HashSet<GameObject>();
            _objectsByProfile[profileName] = objectSet;
        }

        objectSet.Add(gameObject);
    }

    public static void Unregister(GameObject gameObject, string profileName)
    {
        if (string.IsNullOrEmpty(profileName)) return;

        if (_objectsByProfile.TryGetValue(profileName, out var objectSet))
        {
            objectSet.Remove(gameObject);
        }
    }
}