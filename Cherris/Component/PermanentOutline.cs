namespace Cherris;

public class PermanentOutline : Component
{
    public string ProfileName { get; }

    public PermanentOutline(string profileName)
    {
        ProfileName = profileName;
    }

    public override void OnEnable()
    {
        OutlineSystem.Register(GameObject, ProfileName);
    }

    public override void OnDisable()
    {
        OutlineSystem.Unregister(GameObject, ProfileName);
    }
}