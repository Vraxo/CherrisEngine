using Cherris.Components;
using Cherris.Core;
using Cherris.Rendering;
using System.Reflection;

namespace CherrisEditor.Inspectors;

public sealed class ResourceNameSynchronizer
{
    private readonly Editor _editor;

    public ResourceNameSynchronizer(Editor editor)
    {
        _editor = editor;
    }

    public void Sync(Component component, string namePropertyName, string resourceName)
    {
        if (string.IsNullOrEmpty(resourceName) || !namePropertyName.EndsWith("Name"))
        {
            return;
        }

        string targetPropertyName = namePropertyName[..^4];
        PropertyInfo? targetProp = component.GetType().GetProperty(targetPropertyName);

        if (targetProp == null)
        {
            return;
        }

        object? resource = targetProp.PropertyType switch
        {
            Type t when t == typeof(ITexture) => _editor.ResourceManager.GetTexture(resourceName),
            Type t when t == typeof(AudioClip) => _editor.ResourceManager.GetAudioClip(resourceName),
            Type t when t == typeof(Mesh) => _editor.ResourceManager.GetMesh(resourceName),
            _ => null
        };

        if (resource != null)
        {
            targetProp.SetValue(component, resource);
        }
    }
}