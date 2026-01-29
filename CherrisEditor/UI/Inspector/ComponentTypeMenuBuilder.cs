using Cherris.Components;
using System.Collections.Immutable;

namespace CherrisEditor.UI.Inspector;

public class ComponentTypeMenuBuilder
{
    public record MenuEntry(string DisplayName, Type ComponentType);

    public delegate void ScriptCreationHandler(string scriptName);

    private readonly List<MenuEntry> _builtInEntries = [];

    public bool IsInEditMode { get; set; }
    public ScriptCreationHandler? ScriptCreator { get; set; }

    public ComponentTypeMenuBuilder()
    {
        RegisterBuiltInComponents();
    }

    private void RegisterBuiltInComponents()
    {
        Register("Camera", typeof(Camera));
        Register("Light", typeof(Light));
        Register("Audio Source", typeof(AudioSource));
        Register("Audio Listener", typeof(AudioListener));
    }

    public void Register(string displayName, Type componentType)
    {
        if (!typeof(Component).IsAssignableFrom(componentType))
        {
            throw new ArgumentException($"Type {componentType.Name} must inherit from Component");
        }

        _builtInEntries.Add(new MenuEntry(displayName, componentType));
    }

    public ImmutableArray<MenuEntry> GetBuiltInEntries()
    {
        return _builtInEntries.ToImmutableArray();
    }
}