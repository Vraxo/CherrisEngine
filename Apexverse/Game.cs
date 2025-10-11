using Cherris;

namespace Apexverse;

public class Game : Engine
{
    public Game() : base("Veldrid Engine Demo")
    {
    }

    protected override void LoadContent()
    {
        // Pre-load assets that the scene will need
        ResourceManager.LoadInitialAssets();

        // Register custom components that the scene can use
        SceneLoader.RegisterComponentFactory("Spinner", _ => new Spinner());
        SceneLoader.RegisterComponentFactory("PlayerController", _ => new PlayerController());

        // Load the scene from the file and populate the SceneManager
        var loadedObjects = SceneLoader.LoadScene("Assets/Scene.yaml");
        SceneManager.SetScene(loadedObjects);
    }

    protected override void Update(float deltaTime)
    {
        // The base engine Update now handles calling Update on all Scripts
        base.Update(deltaTime);
    }
}