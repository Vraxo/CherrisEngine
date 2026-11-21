using Cherris.Rendering.OpenTK;
using CherrisEditor.Undo;
using ImGuiNET;
using System.Numerics;

namespace CherrisEditor.UI.Viewport;

public class ViewportPanel
{
    private readonly Editor _editor;
    private readonly GizmoController _gizmoController;
    private readonly SceneTabsController _tabsController;
    private readonly ViewportToolbar _toolbar;

    public ViewportPanel(Editor editor, HistoryManager history)
    {
        _editor = editor;
        _gizmoController = new GizmoController(editor, history);
        _tabsController = new SceneTabsController(editor);
        _toolbar = new ViewportToolbar(editor);
    }

    public void Update()
    {
        _gizmoController.Update();
    }

    public void Draw()
    {
        ImGui.PushStyleVar(ImGuiStyleVar.WindowPadding, Vector2.Zero);
        ImGui.Begin("Viewport", ImGuiWindowFlags.NoScrollbar | ImGuiWindowFlags.NoScrollWithMouse);

        if (_tabsController.DrawTabs(out Vector2 viewportPos, out Vector2 viewportSize))
        {
            DrawActiveViewport(viewportPos, viewportSize);
        }

        ImGui.End();
        ImGui.PopStyleVar();
    }

    private void DrawActiveViewport(Vector2 pos, Vector2 size)
    {
        _toolbar.Draw();

        if (_editor.Renderer is not OpenTKRenderer otkRenderer)
        {
            return;
        }

        var surface = new ViewportSurface();
        surface.Draw(otkRenderer, size);
        _editor.IsViewportHovered = surface.IsHovered;

        if (surface.IsHovered)
        {
            HandleViewportClick(pos, surface.Size);
        }

        _gizmoController.Draw(pos, surface.Size);
    }

    private void HandleViewportClick(Vector2 viewportPos, Vector2 viewportSize)
    {
        if (!ImGui.IsMouseClicked(ImGuiMouseButton.Left) || ImGuizmoNET.ImGuizmo.IsOver())
        {
            return;
        }

        Vector2 mousePos = ImGui.GetMousePos();
        var selected = _editor.Selection.PickObject(mousePos, viewportPos, viewportSize);
        _editor.SetSelectedGameObject(selected);
    }
}