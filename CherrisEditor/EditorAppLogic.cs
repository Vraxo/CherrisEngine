using Cherris;
using Cherris.Core;
using Cherris.Core.Logging;
using Cherris.Serialization;
using Cherris.Utils;
using CherrisEditor.UI;
using CherrisEditor.Undo;
using ImGuiNET;
using ImGuizmoNET;
using System.Numerics;

namespace CherrisEditor;

public class EditorAppLogic : IDisposable
{
    private readonly Editor _editor;
    private readonly SceneSerializer _sceneSerializer;
    private readonly HistoryManager _history;
    private readonly ProjectSelector _projectSelector;

    private readonly MenuBar _menuBar;
    private readonly Toolbar _toolbar;
    private readonly ViewportPanel _viewportPanel;
    private readonly OutlinerPanel _outlinerPanel;
    private readonly InspectorPanel _inspectorPanel;
    private readonly ContentBrowserPanel _contentBrowserPanel;
    private readonly ConsolePanel _consolePanel;
    private readonly EditorTextureManager _textureManager;

    public EditorAppLogic(Editor editor, SceneSerializer sceneSerializer)
    {
        _editor = editor;
        _history = editor.History;
        _textureManager = new EditorTextureManager();
        _sceneSerializer = sceneSerializer;
        _projectSelector = new ProjectSelector();

        _projectSelector.OnProjectSelected += OnProjectSelected;

        _menuBar = new MenuBar(editor, _sceneSerializer, _history);
        _toolbar = new Toolbar(editor, _textureManager);
        _viewportPanel = new ViewportPanel(editor, _history);
        _outlinerPanel = new OutlinerPanel(editor);
        _inspectorPanel = new InspectorPanel(editor, _textureManager, _history);
        _contentBrowserPanel = new ContentBrowserPanel(editor, _textureManager);
        _consolePanel = new ConsolePanel();

        EditorTheme.ApplyUnrealEngineStyle();

        LoadIcon("Play", "Icons/Play.png");
        LoadIcon("Pause", "Icons/Pause.png");
        LoadIcon("Stop", "Icons/Stop.png");
        LoadIcon("Reset", "Icons/Reset.png");

        LoadIcon("Component_Transform", "Icons/Components/Transform.png");
        LoadIcon("Component_Camera", "Icons/Components/Camera.png");
        LoadIcon("Component_MeshRenderer", "Icons/Components/MeshRenderer.png");
        LoadIcon("Component_Script", "Icons/Components/Script.png");
        LoadIcon("Component_Light", "Icons/Components/Light.png");
        LoadIcon("Component_AudioSource", "Icons/Components/AudioSource.png");

        ImGuizmo.SetImGuiContext(ImGui.GetCurrentContext());
    }

    private void LoadIcon(string key, string relativePath)
    {
        string? path = EditorResources.Find(relativePath);
        if (path is not null)
        {
            _textureManager.LoadTexture(key, path);
        }
        else
        {
            Logger.Warning($"[Editor] Could not find editor icon '{relativePath}'");
        }
    }

    public Action<float> DrawUI()
    {
        return (deltaTime) =>
        {
            // Prevent ImGui docking layout corruption when window is minimized
            ImGuiIOPtr io = ImGui.GetIO();

            if (io.DisplaySize.X <= 0 || io.DisplaySize.Y <= 0)
            {
                return;
            }

            if (_editor.CurrentProject is null)
            {
                _projectSelector.Draw();
                return;
            }

            SetupDockspace();
            ImGuizmo.BeginFrame();

            _viewportPanel.Draw();
            _outlinerPanel.Draw();
            _consolePanel.Draw();
            _contentBrowserPanel.Draw();
            _inspectorPanel.DrawInspectorPanel();
        };
    }

    private void OnProjectSelected(string projectRoot)
    {
        _editor.LoadProject(projectRoot);
    }

    public void UpdateEditorLogic(float deltaTime)
    {
        if (_editor.CurrentProject is null)
        {
            return;
        }

        _viewportPanel.Update();

        if (_editor.Renderer is not null)
        {
            _editor.Renderer.ShowPhysicsColliders = _editor.State == EditorState.Editing;
        }

        bool ctrl = Input.IsKeyDown(Key.ControlLeft) || Input.IsKeyDown(Key.ControlRight);

        if (ctrl && Input.WasKeyPressed(Key.S))
        {
            Scene? activeScene = _editor.SceneManager.ActiveScene;

            if (activeScene is not null && !string.IsNullOrEmpty(activeScene.FilePath))
            {
                _sceneSerializer.SaveScene(activeScene.GameObjects, activeScene.FilePath);
                activeScene.IsDirty = false;
                Logger.Info($"[Editor] Scene saved to '{activeScene.FilePath}'");
            }
        }

        if (ctrl && Input.WasKeyPressed(Key.Z))
        {
            _history.Undo();
        }

        if (ctrl && Input.WasKeyPressed(Key.Y))
        {
            _history.Redo();
        }
    }

    private void SetupDockspace()
    {
        ImGuiViewportPtr viewport = ImGui.GetMainViewport();
        ImGui.SetNextWindowPos(viewport.Pos);
        ImGui.SetNextWindowSize(viewport.Size);
        ImGui.SetNextWindowViewport(viewport.ID);

        ImGui.PushStyleVar(ImGuiStyleVar.WindowRounding, 0.0f);
        ImGui.PushStyleVar(ImGuiStyleVar.WindowBorderSize, 0.0f);
        ImGui.PushStyleVar(ImGuiStyleVar.WindowPadding, Vector2.Zero);

        ImGuiWindowFlags windowFlags = ImGuiWindowFlags.NoTitleBar | ImGuiWindowFlags.NoCollapse | ImGuiWindowFlags.NoResize | ImGuiWindowFlags.NoMove | ImGuiWindowFlags.NoBringToFrontOnFocus | ImGuiWindowFlags.NoNavFocus | ImGuiWindowFlags.MenuBar | ImGuiWindowFlags.NoBackground;

        ImGui.Begin("MainDockspace", windowFlags);
        ImGui.PopStyleVar(3);

        _menuBar.Draw();

        ImGui.PushStyleVar(ImGuiStyleVar.ItemSpacing, new Vector2(ImGui.GetStyle().ItemSpacing.X, 0f));
        _toolbar.Draw();
        ImGui.PopStyleVar();

        uint dockspaceId = ImGui.GetID("MyDockSpace");
        ImGui.DockSpace(dockspaceId, Vector2.Zero, ImGuiDockNodeFlags.None);

        ImGui.End();
    }

    public void Dispose()
    {
        _contentBrowserPanel.Dispose();
        _outlinerPanel.Dispose();
        _textureManager.Dispose();
    }
}