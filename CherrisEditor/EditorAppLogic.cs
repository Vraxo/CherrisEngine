using Cherris;
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
    private readonly EditorTextureManager _textureManager;
    private readonly ProjectSelector _projectSelector;

    private MenuBar _menuBar;
    private Toolbar _toolbar;
    private ViewportPanel _viewportPanel;
    private OutlinerPanel _outlinerPanel;
    private InspectorPanel _inspectorPanel;
    private ContentBrowserPanel _contentBrowserPanel;
    private ConsolePanel _consolePanel;

    private bool _imGuizmoInitialized;

    private static readonly (string Key, string Path)[] Icons =
    {
        ("Play", "Icons/Play.png"),
        ("Pause", "Icons/Pause.png"),
        ("Stop", "Icons/Stop.png"),
        ("Reset", "Icons/Reset.png"),
        ("Component_Transform", "Icons/Components/Transform.png"),
        ("Component_Camera", "Icons/Components/Camera.png"),
        ("Component_MeshRenderer", "Icons/Components/MeshRenderer.png"),
        ("Component_Script", "Icons/Components/Script.png"),
        ("Component_Light", "Icons/Components/Light.png"),
        ("Component_AudioSource", "Icons/Components/AudioSource.png")
    };

    public EditorAppLogic(Editor editor, SceneSerializer sceneSerializer)
    {
        _editor = editor;
        _history = editor.History;
        _sceneSerializer = sceneSerializer;
        _textureManager = new EditorTextureManager();
        _projectSelector = new ProjectSelector();

        InitializePanels();
        LoadIcons();

        _projectSelector.OnProjectSelected += OnProjectSelected;
        EditorTheme.ApplyUnrealEngineStyle();
    }

    private void InitializePanels()
    {
        _menuBar = new MenuBar(_editor, _sceneSerializer, _history);
        _toolbar = new Toolbar(_editor, _textureManager);
        _viewportPanel = new ViewportPanel(_editor, _history);
        _outlinerPanel = new OutlinerPanel(_editor);
        _inspectorPanel = new InspectorPanel(_editor, _textureManager, _history);
        _contentBrowserPanel = new ContentBrowserPanel(_editor, _textureManager);
        _consolePanel = new ConsolePanel();
    }

    private void LoadIcons()
    {
        foreach (var (key, path) in Icons)
        {
            string? fullPath = EditorResources.Find(path);
            if (fullPath is not null)
            {
                _textureManager.LoadTexture(key, fullPath);
            }
            else
            {
                Logger.Warning($"[Editor] Could not find editor icon '{path}'");
            }
        }
    }

    public void Draw(float deltaTime)
    {
        InitializeImGuizmoOnce();

        if (IsWindowMinimized())
        {
            return;
        }

        if (_editor.CurrentProject is null)
        {
            _projectSelector.Draw();
            return;
        }

        SetupDockspace();

        _viewportPanel.Draw();
        _outlinerPanel.Draw();
        _consolePanel.Draw();
        _contentBrowserPanel.Draw();
        _inspectorPanel.DrawInspectorPanel();
    }

    private void InitializeImGuizmoOnce()
    {
        if (_imGuizmoInitialized)
        {
            return;
        }

        ImGuizmo.SetImGuiContext(ImGui.GetCurrentContext());
        _imGuizmoInitialized = true;
    }

    private static bool IsWindowMinimized()
    {
        var io = ImGui.GetIO();
        return io.DisplaySize.X <= 0 || io.DisplaySize.Y <= 0;
    }

    public void UpdateEditorLogic(float deltaTime)
    {
        if (_editor.CurrentProject is null)
        {
            return;
        }

        _viewportPanel.Update();
        SyncDebugRenderingState();
        HandleEditorInput();
    }

    private void SyncDebugRenderingState()
    {
        if (_editor.Renderer is null)
        {
            return;
        }

        _editor.Renderer.ShowPhysicsColliders = _editor.State == EditorState.Editing;
    }

    private void HandleEditorInput()
    {
        bool ctrl = Input.IsKeyDown(Key.ControlLeft) || Input.IsKeyDown(Key.ControlRight);

        if (!ctrl)
        {
            return;
        }

        if (Input.WasKeyPressed(Key.S))
        {
            SaveScene();
        }

        if (Input.WasKeyPressed(Key.Z))
        {
            _history.Undo();
        }

        if (Input.WasKeyPressed(Key.Y))
        {
            _history.Redo();
        }
    }

    private void SaveScene()
    {
        var activeScene = _editor.SceneManager.ActiveScene;
        if (activeScene is null || string.IsNullOrEmpty(activeScene.FilePath))
        {
            return;
        }

        _sceneSerializer.SaveScene(activeScene.GameObjects, activeScene.FilePath);
        activeScene.IsDirty = false;
        Logger.Info($"[Editor] Scene saved to '{activeScene.FilePath}'");
    }

    private void OnProjectSelected(string projectRoot)
    {
        _editor.LoadProject(projectRoot);
    }

    private void SetupDockspace()
    {
        ImGuiViewportPtr viewport = ImGui.GetMainViewport();
        ImGui.SetNextWindowPos(viewport.Pos);
        ImGui.SetNextWindowSize(viewport.Size);
        ImGui.SetNextWindowViewport(viewport.ID);

        ImGuiWindowFlags windowFlags =
            ImGuiWindowFlags.NoTitleBar |
            ImGuiWindowFlags.NoCollapse |
            ImGuiWindowFlags.NoResize |
            ImGuiWindowFlags.NoMove |
            ImGuiWindowFlags.NoBringToFrontOnFocus |
            ImGuiWindowFlags.NoNavFocus |
            ImGuiWindowFlags.MenuBar |
            ImGuiWindowFlags.NoBackground;

        ImGui.PushStyleVar(ImGuiStyleVar.WindowRounding, 0.0f);
        ImGui.PushStyleVar(ImGuiStyleVar.WindowBorderSize, 0.0f);
        ImGui.PushStyleVar(ImGuiStyleVar.WindowPadding, Vector2.Zero);

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