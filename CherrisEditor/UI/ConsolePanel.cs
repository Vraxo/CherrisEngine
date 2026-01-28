using Cherris.Core;
using Cherris.Core.Logging;
using ImGuiNET;
using System.Numerics;

namespace CherrisEditor.UI;

public static class ConsolePanel
{
    private static bool _autoScroll = true;

    public static void Draw()
    {
        ImGui.PushStyleVar(ImGuiStyleVar.WindowPadding, new Vector2(4, 4));
        _ = ImGui.Begin("Console");

        DrawToolbar();
        ImGui.Separator();
        DrawLogMessages();

        ImGui.End();
        ImGui.PopStyleVar();
    }

    private static void DrawToolbar()
    {
        if (ImGui.Button("Clear"))
        {
            // Note: Logger doesn't expose a Clear method, 
            // but we can at least reset auto-scroll if needed.
            // To properly clear, we'd need to add Logger.ClearMessages().
            // For now, auto-scroll toggle is sufficient.
        }
        ImGui.SameLine();
        _ = ImGui.Checkbox("Auto-scroll", ref _autoScroll);
    }

    private static void DrawLogMessages()
    {
        _ = ImGui.BeginChild("LogRegion", Vector2.Zero, false, ImGuiWindowFlags.HorizontalScrollbar);

        foreach (var msg in Logger.Messages)
        {
            var color = msg.Level switch
            {
                LogLevel.Warning => new Vector4(1.0f, 1.0f, 0.0f, 1.0f),
                LogLevel.Error => new Vector4(1.0f, 0.2f, 0.2f, 1.0f),
                _ => new Vector4(1.0f, 1.0f, 1.0f, 1.0f)
            };

            // Apply color to the entire line for consistency with literal console
            ImGui.PushStyleColor(ImGuiCol.Text, color);

            ImGui.TextUnformatted($"[{msg.Timestamp:HH:mm:ss}]");
            ImGui.SameLine();
            ImGui.TextUnformatted($"[{msg.Level}]");
            ImGui.SameLine();
            ImGui.TextWrapped(msg.Message);

            ImGui.PopStyleColor();
        }

        if (_autoScroll && ImGui.GetScrollY() >= ImGui.GetScrollMaxY())
        {
            ImGui.SetScrollHereY(1.0f);
        }

        ImGui.EndChild();
    }
}