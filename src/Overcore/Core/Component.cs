using Overcore.Core;
using Overcore.Utils;

namespace Overcore.Components;

public abstract class Component
{
    [HideInInspector]
    public GameObject? GameObject { get; internal set; }

    /// <summary>
    /// The inspector's header checkbox, Unity's <c>enabled</c>: false switches the component off without taking it
    /// off the object — a renderer stops drawing, a light stops lighting, a script stops ticking — and the state
    /// is saved with the scene.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Hidden from the property list because the inspector draws it in the header, and written and read by the
    /// scene serializer itself for every component, whatever its factory reads (<c>SceneSerializer</c>,
    /// <c>SceneLoader</c>).
    /// </para>
    /// <para>
    /// This is the author's decision. Whether a script runs <em>right now</em> is a second question, answered by
    /// <see cref="Script.IsSuspended"/>: the desktop editor holds scripts back while editing, and the scene holds
    /// one back after it threw, without touching this flag.
    /// </para>
    /// </remarks>
    [HideInInspector]
    public bool Enabled { get; set; } = true;
}
