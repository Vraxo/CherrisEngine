using Cherris.Core;
using ImGuiNET;
using System.Collections.Concurrent;
using System.Numerics;

namespace CherrisEditor.UI;

public static class ConsolePanel
{
    private static readonly ConcurrentQueue<LogMessage> _logMessages = new();
    private static bool _autoScroll = true;

    static ConsolePanel()
    {
        Logger.OnMessageLogged += HandleLogMessage;
    }

    private static void HandleLogMessage(LogMessage message)
    {
        _logMessages.Enqueue(message);
    }

    public static void Draw()
    {
        ImGui.PushStyleVar(ImGuiStyleVar.WindowPadding, new Vector2(4, 4));
        ImGui.Begin("Console");

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
            _logMessages.Clear();
        }
        ImGui.SameLine();
        ImGui.Checkbox("Auto-scroll", ref _autoScroll);
    }

    private static void DrawLogMessages()
    {
        ImGui.BeginChild("LogRegion", Vector2.Zero, false, ImGuiWindowFlags.HorizontalScrollbar);

        foreach (var msg in _logMessages)
        {
            var color = msg.Level switch
            {
                LogLevel.Warning => new Vector4(1.0f, 1.0f, 0.0f, 1.0f),
                LogLevel.Error => new Vector4(1.0f, 0.2f, 0.2f, 1.0f),
                _ => new Vector4(1.0f, 1.0f, 1.0f, 1.0f)
            };

            ImGui.TextColored(color, $"[{msg.Timestamp:HH:mm:ss}]");
            ImGui.SameLine();
            ImGui.TextUnformatted($"[{msg.Level}]");
            ImGui.SameLine();
            ImGui.TextWrapped(msg.Message);
        }

        if (_autoScroll && ImGui.GetScrollY() >= ImGui.GetScrollMaxY())
        {
            ImGui.SetScrollHereY(1.0f);
        }

        ImGui.EndChild();
    }
}