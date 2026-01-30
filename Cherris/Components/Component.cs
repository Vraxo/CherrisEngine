using Cherris.Core;
using Cherris.Utils;

namespace Cherris.Components;

public abstract class Component
{
    [HideInInspector]
    public GameObject? GameObject { get; internal set; }
}