using Cherris.Core;

namespace Cherris.Components;

public abstract class Component
{
    [HideInInspector]
    public GameObject GameObject { get; internal set; }
}