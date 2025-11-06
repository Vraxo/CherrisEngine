namespace Cherris.Components;

public abstract class Script : Component
{
	public bool Enabled { get; set; } = true;
	public virtual void Start() { }
	public virtual void Update(float deltaTime) { }
}