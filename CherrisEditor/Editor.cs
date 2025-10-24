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

    public void SetSelectedGameObject(GameObject? go)
    {
        SelectedGameObject = go;
    }

    public GameObject? GetSelectedGameObject()
    {
        return SelectedGameObject;
    }

    protected override void LoadContent()
    {
        ResourceManager.LoadInitialAssets();
        RegisterComponents();
        LoadScene();
    }

    private void RegisterComponents()
    {
        SceneLoader.RegisterComponentFactory("Spinner", _ => new Spinner());
        SceneLoader.RegisterComponentFactory("PlayerController", _ => new PlayerController());
    }

    private void LoadScene()
    {
        List<GameObject> loadedObjects = SceneLoader.LoadScene("Assets/Scene.yaml");
        SceneManager.SetScene(loadedObjects);
    }

    protected override void OnStart()
    {
        _editorAppLogic = new(this);
        OnDrawUI = _editorAppLogic.DrawUI();

        if (SceneManager.MainCamera?.GameObject is null)
        {
            return;
        }

        SetupEditorCamera();
    }

    private void SetupEditorCamera()
    {
        GameObject cameraGo = SceneManager.MainCamera.GameObject;

        var playerController = cameraGo.GetComponent<PlayerController>();

        if (playerController is not null)
        {
            cameraGo.RemoveComponent<PlayerController>();
        }

        cameraGo.AddComponent(new EditorController());
    }

    protected override void Update(float deltaTime)
    {
        _editorAppLogic?.UpdateEditorLogic(deltaTime);
        SceneManager.Update(deltaTime);

        base.Update(deltaTime);
    }
}