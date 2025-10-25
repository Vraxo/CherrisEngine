using Cherris;
using ImGuiNET;
using System;

namespace CherrisEditor.UI;

public class MenuBar
{
    private readonly SceneSerializer _sceneSerializer;
    private readonly Editor _editor;

    public MenuBar(Editor editor, SceneSerializer sceneSerializer)
    {
        _editor = editor;
        _sceneSerializer = sceneSerializer;
    }

    public void Draw()
    {
        if (!ImGui.BeginMenuBar()) return;

        if (ImGui.BeginMenu("File"))
        {
            if (ImGui.MenuItem("Save"))
            {
                if (!string.IsNullOrEmpty(_editor.CurrentScenePath))
                {
                    _sceneSerializer.SaveScene(_editor.SceneManager.GameObjects, _editor.CurrentScenePath);
                    Console.WriteLine($"[Editor] Scene saved to '{_editor.CurrentScenePath}'");
                }
            }
            ImGui.Separator();
            if (ImGui.MenuItem("Exit")) { Environment.Exit(0); }
            ImGui.EndMenu();
        }

        ImGui.EndMenuBar();
    }
}