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
        DrawLogMessages();

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

    private void DrawLogMessages()
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