using Cherris.Core;
using Cherris.Core.Logging;
using ImGuiNET;
using System.Numerics;

namespace CherrisEditor.UI;

public class ConsolePanel
{
    private bool _autoScroll = true;

    public void Draw()
    {
        ImGui.PushStyleVar(ImGuiStyleVar.WindowPadding, new Vector2(4, 4));
        _ = ImGui.Begin("Console");

        DrawToolbar();
        ImGui.Separator();
        DrawLogContent();

        ImGui.End();
        ImGui.PopStyleVar();
    }

    private void DrawToolbar()
    {
        if (ImGui.Button("Clear"))
        {
            Logger.Clear();
        }

        ImGui.SameLine();
        _ = ImGui.Checkbox("Auto-scroll", ref _autoScroll);
    }

    private void DrawLogContent()
    {
        _ = ImGui.BeginChild("LogRegion", Vector2.Zero, false);

        foreach (LogMessage message in Logger.Messages)
        {
            RenderMessage(message);
        }

        HandleAutoScroll();
        ImGui.EndChild();
    }

    private static void RenderMessage(LogMessage message)
    {
        ImGui.PushStyleColor(ImGuiCol.Text, GetMessageColor(message.Level));

        ImGui.TextUnformatted($"[{message.Timestamp:HH:mm:ss}]");
        ImGui.SameLine();
        ImGui.TextUnformatted($"[{message.Level}]");
        ImGui.SameLine();
        ImGui.TextWrapped(message.Message);

        ImGui.PopStyleColor();
    }

    private static Vector4 GetMessageColor(LogLevel level)
    {
        return level switch
        {
            LogLevel.Warning => new(1.0f, 1.0f, 0.0f, 1.0f),
            LogLevel.Error => new(1.0f, 0.2f, 0.2f, 1.0f),
            _ => new(1.0f, 1.0f, 1.0f, 1.0f)
        };
    }

    private void HandleAutoScroll()
    {
        if (!_autoScroll)
        {
            return;
        }

        if (ImGui.GetScrollY() >= ImGui.GetScrollMaxY())
        {
            ImGui.SetScrollHereY(1.0f);
        }
    }
}