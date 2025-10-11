using System.Diagnostics;
using System.Numerics;
using Veldrid.Sdl2;
using Veldrid.StartupUtilities;
using Veldrid;

namespace Cherris;

public abstract class Component
{
    public GameObject GameObject { get; internal set; }
}

public abstract class Script : Component
{
    public virtual void Start() { }
    public virtual void Update(float deltaTime) { }
}