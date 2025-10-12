using Cherris;

namespace Apexverse;

public class Game : Engine
{
    public Game(EngineMode mode) : base("Veldrid Engine Demo", mode)
    {
    }

    protected override void LoadContent()
    {
        // Pre-load assets that the scene will need
        ResourceManager.LoadInitialAssets();

        // Register custom components that the scene can use
        SceneLoader.RegisterComponentFactory("Spinner", _ => new Spinner());
        SceneLoader.RegisterComponentFactory("PlayerController", _ => new PlayerController());
        SceneLoader.RegisterComponentFactory("PermanentOutline", _ => new PermanentOutline());

        // Load the scene from the file and populate the SceneManager
        var loadedObjects = SceneLoader.LoadScene("Assets/Scene.yaml");
        SceneManager.SetScene(loadedObjects);
    }

    protected override void OnStart()
    {
        if (Mode == EngineMode.Editor && SceneManager.MainCamera?.GameObject != null)
        {
            var cameraGo = SceneManager.MainCamera.GameObject;

            // If the scene camera is the player, remove its controller
            // so it doesn't fight with the new editor controller.
            var playerController = cameraGo.GetComponent<PlayerController>();
            if (playerController != null)
            {
                cameraGo.RemoveComponent<PlayerController>();
            }

            var editorController = cameraGo.AddComponent(new EditorController());
            RegisterEditorController(editorController);
        }
    }

    protected override void Update(float deltaTime)
    {
        // The base engine Update now handles calling Update on all Scripts
        base.Update(deltaTime);
    }
}