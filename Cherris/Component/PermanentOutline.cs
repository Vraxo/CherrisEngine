namespace Cherris;

public class PermanentOutline : Component
{
    public override void OnEnable()
    {
        OutlineSystem.Register(GameObject);
    }

    public override void OnDisable()
    {
        OutlineSystem.Unregister(GameObject);
    }
}