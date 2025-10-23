using Cherris;
using Cherris.Rendering;
using DirectUI;
using DirectUI.Core;
using System;
using System.Numerics;

namespace Apexverse;

public class EditorAppLogic : IAppLogic
{
    private readonly IWindowHost _host;
    private float _sliderValue = 50f;

    public EditorAppLogic(IWindowHost host)
    {
        _host = host;
    }

    public void DrawUI(UIContext context)
    {
        UI.Text("hello_world", $"Hello from DirectUI on Cherris! Scale: {_host.AppEngine.UIScale:F2}", new Vector2(400, 25), new ButtonStyle { FontSize = 16 });

        UI.BeginHBoxContainer("hbox1", new Vector2(0, 30), 10);
        if (UI.Button("my_button", "Click Me!", new Vector2(100, 30)))
        {
            Console.WriteLine("Button clicked!");
        }
        UI.Text("label1", "A UI Label", new Vector2(100, 30));
        UI.EndHBoxContainer();

        _sliderValue = UI.HSlider("slider1", _sliderValue, 0, 100, new Vector2(200, 20), new Vector2(0, 70));

    }
    public void SaveState() { }
}


public class Game : Engine
{
    public Game(EngineMode mode, GraphicsAPI api) : base("Cherris Engine (DirectUI Integration)", mode, api)
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

    protected override void OnStart()
    {
        if (Mode == EngineMode.Editor)
        {
            if (_backend is OpenTKBackend otkBackend && _backend is IWindowHost host)
            {
                var uiLogic = new EditorAppLogic(host);
                otkBackend.SetUILogic(uiLogic);
            }

            if (SceneManager.MainCamera?.GameObject != null)
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
    }

    protected override void Update(float deltaTime)
    {
        // The base engine Update now handles calling Update on all Scripts
        base.Update(deltaTime);
    }
}