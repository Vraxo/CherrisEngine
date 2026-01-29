using Cherris;
using Cherris.Core;
using CherrisEditor.Undo;
using ImGuiNET;
using System.Numerics;

namespace CherrisEditor.UI;

public class ViewportPanel
{
    private readonly Editor _editor;
    private readonly GizmoController _gizmoController;
    private readonly ViewportSurface _viewportSurface;

    public ViewportPanel(Editor editor, HistoryManager history)
    {
        _editor = editor;
        _gizmoController = new GizmoController(editor, history);
        _viewportSurface = new ViewportSurface();
    }

    public void Update()
    {
        _gizmoController.Update();
    }

    public void Draw()
    {
        ImGui.PushStyleVar(ImGuiStyleVar.WindowPadding, Vector2.Zero);
        ImGui.Begin("Viewport", ImGuiWindowFlags.NoScrollbar | ImGuiWindowFlags.NoScrollWithMouse);

        DrawSceneTabs();

        ImGui.End();
        ImGui.PopStyleVar();
    }

    private void DrawSceneTabs()
    {
        var openScenes = _editor.SceneManager.OpenScenes.ToList();
        var activeScene = _editor.SceneManager.ActiveScene;

        if (!ImGui.BeginTabBar("SceneTabBar", ImGuiTabBarFlags.Reorderable))
        {
            return;
        }

        if (openScenes.Count == 0)
        {
            DrawEmptyTab();
        }

        foreach (var scene in openScenes)
        {
            DrawSceneTab(scene, activeScene);
        }

        ImGui.EndTabBar();
    }

    private static void DrawEmptyTab()
    {
        if (ImGui.BeginTabItem("No Scene"))
        {
            ImGui.Text("No scene loaded. Open a scene from the Content Browser.");
            ImGui.EndTabItem();
        }
    }

    private void DrawSceneTab(Scene scene, Scene activeScene)
    {
        bool isOpen = true;
        var flags = CalculateTabFlags(scene, activeScene);

        if (!ImGui.BeginTabItem(scene.Name, ref isOpen, flags))
        {
            return;
        }

        if (ImGui.IsItemClicked(ImGuiMouseButton.Left))
        {
            _editor.SceneManager.SetActiveScene(scene);
        }

        if (scene == activeScene)
        {
            DrawActiveSceneViewport();
        }

        ImGui.EndTabItem();

        if (!isOpen)
        {
            _editor.SceneManager.CloseScene(scene);
        }
    }

    private static ImGuiTabItemFlags CalculateTabFlags(Scene scene, Scene activeScene)
    {
        var flags = scene.IsDirty ? ImGuiTabItemFlags.UnsavedDocument : ImGuiTabItemFlags.None;
        if (scene == activeScene)
        {
            flags |= ImGuiTabItemFlags.SetSelected;
        }

        return flags;
    }

    private void DrawActiveSceneViewport()
    {
        DrawToolbar();

        Vector2 viewportPos = ImGui.GetCursorScreenPos();
        Vector2 viewportSize = ImGui.GetContentRegionAvail();

        if (_editor.Renderer is not OpenTKRenderer otkRenderer)
        {
            return;
        }

        _viewportSurface.Draw(otkRenderer, viewportSize);
        _editor.IsViewportHovered = _viewportSurface.IsHovered;

        if (_viewportSurface.IsHovered)
        {
            HandleViewportInput(viewportPos, _viewportSurface.Size);
        }

        _gizmoController.Draw(viewportPos, _viewportSurface.Size);
    }

    private void DrawToolbar()
    {
        ImGui.BeginChild("ViewportToolbar", new Vector2(0, ImGui.GetFrameHeightWithSpacing()), false,
            ImGuiWindowFlags.NoScrollbar | ImGuiWindowFlags.NoScrollWithMouse);

        ImGui.PushStyleVar(ImGuiStyleVar.FramePadding, new Vector2(4, 4));
        DrawOptionsDropdown();
        ImGui.PopStyleVar();

        ImGui.EndChild();
    }

    private void DrawOptionsDropdown()
    {
        ImGui.PushStyleColor(ImGuiCol.Button, new Vector4(0.2f, 0.2f, 0.2f, 0.8f));
        ImGui.PushStyleColor(ImGuiCol.ButtonHovered, new Vector4(0.3f, 0.3f, 0.3f, 0.9f));
        ImGui.PushStyleColor(ImGuiCol.ButtonActive, new Vector4(0.25f, 0.25f, 0.25f, 0.9f));

        if (ImGui.Button("Options"))
        {
            ImGui.OpenPopup("ViewportOptionsPopup");
        }

        if (ImGui.BeginPopup("ViewportOptionsPopup"))
        {
            DrawOptionsMenu();
            ImGui.EndPopup();
        }

        ImGui.PopStyleColor(3);
    }

    private void DrawOptionsMenu()
    {
        bool showGrid = _editor.Renderer.ShowGrid;
        if (ImGui.Checkbox("Show Grid", ref showGrid))
        {
            _editor.Renderer.ShowGrid = showGrid;
        }
    }

    private void HandleViewportInput(Vector2 viewportPos, Vector2 viewportSize)
    {
        if (!ImGui.IsMouseClicked(ImGuiMouseButton.Left) || ImGuizmoNET.ImGuizmo.IsUsing() || ImGuizmoNET.ImGuizmo.IsOver())
        {
            return;
        }

        Vector2 mousePos = ImGui.GetMousePos();
        var selected = _editor.Selection.PickObject(mousePos, viewportPos, viewportSize);
        _editor.SetSelectedGameObject(selected);
    }
}