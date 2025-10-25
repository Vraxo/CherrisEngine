using ImGuiNET;

namespace CherrisEditor.UI;

public static class ConsolePanel
{
    public static void Draw()
    {
        ImGui.Begin("Console");
        ImGui.Text("Log messages will appear here...");
        ImGui.End();
    }
}