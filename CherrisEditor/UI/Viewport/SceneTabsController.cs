using Cherris.Core;
using ImGuiNET;
using System.Numerics;

namespace CherrisEditor.UI.Viewport;

public class SceneTabsController
{
    private readonly Editor _editor;

    public SceneTabsController(Editor editor)
    {
        _editor = editor;
    }

    public bool DrawTabs(out Vector2 viewportPos, out Vector2 viewportSize)
    {
        viewportPos = Vector2.Zero;
        viewportSize = Vector2.Zero;

        var openScenes = _editor.SceneManager.OpenScenes.ToList();

        if (!ImGui.BeginTabBar("SceneTabBar", ImGuiTabBarFlags.Reorderable))
        {
            return false;
        }

        bool hasActiveViewport = false;

        foreach (var scene in openScenes)
        {
            if (DrawSceneTab(scene))
            {
                hasActiveViewport = true;
                viewportPos = ImGui.GetCursorScreenPos();
                viewportSize = ImGui.GetContentRegionAvail();
            }
        }

        ImGui.EndTabBar();
        return hasActiveViewport;
    }

    private bool DrawSceneTab(Scene scene)
    {
        bool isOpen = true;
        var flags = scene.IsDirty ? ImGuiTabItemFlags.UnsavedDocument : ImGuiTabItemFlags.None;

        if (scene == _editor.SceneManager.ActiveScene)
        {
            flags |= ImGuiTabItemFlags.SetSelected;
        }

        bool visible = ImGui.BeginTabItem(scene.Name, ref isOpen, flags);

        if (ImGui.IsItemClicked())
        {
            _editor.SceneManager.SetActiveScene(scene);
        }

        if (visible)
        {
            if (scene != _editor.SceneManager.ActiveScene)
            {
                ImGui.EndTabItem();
                return false;
            }
            return true;
        }

        if (!isOpen)
        {
            _editor.SceneManager.CloseScene(scene);
        }

        return false;
    }
}