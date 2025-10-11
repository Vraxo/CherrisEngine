namespace Cherris;

public abstract class Component
{
    public GameObject GameObject { get; internal set; }

    public virtual void OnEnable() { }
    public virtual void OnDisable() { }
}

public abstract class Script : Component
{
    public virtual void Start() { }
    public virtual void Update(float deltaTime) { }
}