namespace Cherris;

public abstract class Component
{
    [HideInInspector]
    public GameObject GameObject { get; internal set; }
}

public abstract class Script : Component
{
    public bool Enabled { get; set; } = true;
    public virtual void Start() { }
    public virtual void Update(float deltaTime) { }
}