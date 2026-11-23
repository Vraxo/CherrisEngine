namespace Cherris.Components;

/// <summary>
/// The components an editor can add to an object, in the order the Add Component menu shows them.
/// </summary>
/// <remarks>
/// Lives with the components themselves rather than in an editor, because both front ends offer the same
/// list: the ImGui inspector builds its menu from <see cref="BuiltIn"/> and the Photino inspector queues an
/// <c>AddComponentCommand</c> naming one of these entries. Adding an entry here is what makes a component
/// appendable in either window.
/// </remarks>
public static class ComponentCatalog
{
    /// <summary>One menu entry: the name to show and the type it creates.</summary>
    /// <param name="DisplayName">Name in the menu, and the name a command quotes back.</param>
    /// <param name="ComponentType">A concrete <see cref="Component"/> type with a parameterless constructor.</param>
    public sealed record Entry(string DisplayName, Type ComponentType)
    {
        /// <summary>Creates an instance for <see cref="Cherris.Core.GameObject.AddComponent{T}"/>.</summary>
        public Component Create()
        {
            return (Component)Activator.CreateInstance(ComponentType)!;
        }
    }

    /// <summary>The built-in components, in menu order.</summary>
    public static IReadOnlyList<Entry> BuiltIn { get; } =
    [
        new Entry("Camera", typeof(Camera)),
        new Entry("Light", typeof(Light)),
        new Entry("Audio Source", typeof(AudioSource)),
        new Entry("Audio Listener", typeof(AudioListener))
    ];

    /// <summary>Finds an entry by the name the menu shows.</summary>
    /// <param name="displayName">Name as shown, case-sensitive.</param>
    public static Entry? Find(string? displayName)
    {
        return String.IsNullOrWhiteSpace(displayName)
            ? null
            : BuiltIn.FirstOrDefault(entry => String.Equals(entry.DisplayName, displayName, StringComparison.Ordinal));
    }
}
