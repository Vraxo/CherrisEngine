using Cherris;
using Apexverse;

namespace CherrisEditor;

public class Editor : Engine
{
    private EditorAppLogic? _editorAppLogic;

    public Editor(GraphicsAPI api) : base("Cherris Editor", false, api)
    {
        Exposure = 0.5f;
    }

    // These methods allow EditorAppLogic to interact with the protected SelectedGameObject
    public void SetSelectedGameObject(GameObject? go) => SelectedGameObject = go;
    public GameObject? GetSelectedGameObject() => SelectedGameObject;

    protected override void LoadContent()
    {
        // Load the game's assets and components
        ResourceManager.LoadInitialAssets();

        // Register game-specific components from the Apexverse library
        SceneLoader.RegisterComponentFactory("Spinner", _ => new Spinner());
        SceneLoader.RegisterComponentFactory("PlayerController", _ => new PlayerController());

        // Load the scene to be edited
        var loadedObjects = SceneLoader.LoadScene("Assets/Scene.yaml");
        SceneManager.SetScene(loadedObjects);
    }

    protected override void OnStart()
    {
        // Setup the editor UI and camera controller
        _editorAppLogic = new EditorAppLogic(this);
        OnDrawUI = _editorAppLogic.DrawUI();

        if (SceneManager.MainCamera?.GameObject is not null)
        {
            var cameraGo = SceneManager.MainCamera.GameObject;

            // If the scene camera is the player, remove its controller
            // so it doesn't fight with the new editor controller.
            var playerController = cameraGo.GetComponent<PlayerController>();
            if (playerController is not null)
            {
                cameraGo.RemoveComponent<PlayerController>();
            }

            // Add our own editor-specific camera controller
            cameraGo.AddComponent(new EditorController());
        }
    }

    protected override void Update(float deltaTime)
    {
        // Run editor-specific logic like picking
        _editorAppLogic?.UpdateEditorLogic(deltaTime);

        // Run game logic simulations (e.g., animations, physics)
        SceneManager.Update(deltaTime);

        // Run base engine updates (UI rendering, snapshots, etc.)
        base.Update(deltaTime);
    }
}