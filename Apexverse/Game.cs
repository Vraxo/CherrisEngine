using Cherris;

namespace Apexverse;

public class Game : Engine
{
    public Game(GraphicsAPI api) : base("Apexverse", true, api)
    {
        Exposure = 0.5f;
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
        // Update all game scripts and logic
        SceneManager.Update(deltaTime);

        // The base engine Update now handles calling Update on the UI controller, etc.
        base.Update(deltaTime);
    }
}