using Cherris;
using CherrisEditor.UI;
using ImGuiNET;
using ImGuizmoNET;
using System;
using System.Linq;
using System.Numerics;

namespace CherrisEditor;

public class EditorAppLogic : IDisposable
{
    private readonly Editor _editor;
    private readonly SceneSerializer _sceneSerializer;

    private readonly MenuBar _menuBar;
    private readonly Toolbar _toolbar;
    private readonly ViewportPanel _viewportPanel;
    private readonly OutlinerPanel _outlinerPanel;
    private readonly InspectorPanel _inspectorPanel;
    private readonly ContentBrowserPanel _contentBrowserPanel;
    private readonly EditorTextureManager _textureManager;

    public EditorAppLogic(Editor editor)
    {
        _editor = editor;
        _textureManager = new EditorTextureManager();
        _sceneSerializer = new SceneSerializer();

        _menuBar = new MenuBar(editor, _sceneSerializer);
        _toolbar = new Toolbar(editor, _textureManager);
        _viewportPanel = new ViewportPanel(editor);
        _outlinerPanel = new OutlinerPanel(editor);
        _inspectorPanel = new InspectorPanel(editor, _textureManager);
        _contentBrowserPanel = new ContentBrowserPanel(editor, _textureManager);

        EditorTheme.ApplyUnrealEngineStyle();

        _textureManager.LoadTexture("Play", "Assets/Icons/play.png");
        _textureManager.LoadTexture("Pause", "Assets/Icons/pause.png");
        _textureManager.LoadTexture("Stop", "Assets/Icons/stop.png");
        _textureManager.LoadTexture("Restart", "Assets/Icons/restart.png");
        _textureManager.LoadTexture("Reset", "Assets/Icons/reset.png");

        // Load component icons
        _textureManager.LoadTexture("Component_Transform", "Assets/Icons/Components/transform.png");
        _textureManager.LoadTexture("Component_Camera", "Assets/Icons/Components/camera.png");
        _textureManager.LoadTexture("Component_MeshRenderer", "Assets/Icons/Components/mesh_renderer.png");
        _textureManager.LoadTexture("Component_Script", "Assets/Icons/Components/script.png");


        ImGuizmo.SetImGuiContext(ImGui.GetCurrentContext());
    }

    public Action<float> DrawUI()
    {
        return (deltaTime) =>
        {
            SetupDockspace();
            ImGuizmo.BeginFrame();

            _viewportPanel.Draw();
            _outlinerPanel.Draw();
            ConsolePanel.Draw();
            _contentBrowserPanel.Draw();
            _inspectorPanel.DrawInspectorPanel();
        };
    }

    public void UpdateEditorLogic(float deltaTime)
    {
        _viewportPanel.Update();
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
        _textureManager.Dispose();
    }
}