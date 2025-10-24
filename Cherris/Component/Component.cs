namespace Cherris;

public abstract class Component
{
    [HideInInspector]
    public GameObject GameObject { get; internal set; }
}

public abstract class Script : Component
{
    public virtual void Start() { }
    public virtual void Update(float deltaTime) { }
}