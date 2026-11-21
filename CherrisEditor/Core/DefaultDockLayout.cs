using System.Numerics;
using System.Text;

namespace CherrisEditor;

/// <summary>Builds the initial Dear ImGui docking settings without changing saved layouts.</summary>
public static class DefaultDockLayout
{
    private const string Marker = "[Window][CherrisDefaultLayoutV1]";
    private static readonly string[] Panels = ["Viewport", "Hierarchy", "Inspector", "Content Browser", "Console"];

    public readonly record struct NodeIds(uint Root, uint Left, uint Rest, uint Middle, uint Right, uint Viewport, uint Bottom);

    public static bool NeedsDefault(string settings)
    {
        // The marker also preserves an intentional layout where every panel was undocked.
        if (settings.Contains(Marker, StringComparison.Ordinal)) return false;

        bool panel = false;
        foreach (string line in settings.Split('\n'))
        {
            string trimmed = line.Trim();
            if (trimmed.StartsWith('['))
                panel = Panels.Any(name => trimmed.Equals($"[Window][{name}]", StringComparison.Ordinal));
            else if (panel && trimmed.StartsWith("DockId=0x", StringComparison.OrdinalIgnoreCase))
            {
                string id = trimmed[9..].Split(',')[0];
                if (uint.TryParse(id, System.Globalization.NumberStyles.HexNumber, null, out uint value) && value != 0)
                    return false;
            }
        }
        return true;
    }

    public static string Create(NodeIds ids, Vector2 position, Vector2 size)
    {
        int width = Math.Clamp((int)size.X, 1, 32767);
        int height = Math.Clamp((int)size.Y, 1, 32767);
        int x = Math.Clamp((int)position.X, -32768, 32767);
        int y = Math.Clamp((int)position.Y, -32768, 32767);
        int left = Math.Max(1, (int)(width * 0.20f));
        int right = Math.Max(1, (int)(width * 0.24f));
        int middle = Math.Max(1, width - left - right);
        int top = Math.Max(1, (int)(height * 0.72f));
        int bottom = Math.Max(1, height - top);

        var result = new StringBuilder();
        Window("Hierarchy", ids.Left);
        Window("Inspector", ids.Right);
        Window("Viewport", ids.Viewport);
        Window("Content Browser", ids.Bottom, 0);
        Window("Console", ids.Bottom, 1);
        result.AppendLine(Marker).AppendLine("Pos=0,0").AppendLine("Size=1,1").AppendLine("Collapsed=0").AppendLine();
        result.AppendLine("[Docking][Data]");
        result.AppendLine($"DockSpace ID=0x{ids.Root:X8} Pos={x},{y} Size={width},{height} Split=X");
        result.AppendLine($"  DockNode ID=0x{ids.Left:X8} Parent=0x{ids.Root:X8} SizeRef={left},{height}");
        result.AppendLine($"  DockNode ID=0x{ids.Rest:X8} Parent=0x{ids.Root:X8} SizeRef={middle + right},{height} Split=X");
        result.AppendLine($"    DockNode ID=0x{ids.Middle:X8} Parent=0x{ids.Rest:X8} SizeRef={middle},{height} Split=Y");
        result.AppendLine($"      DockNode ID=0x{ids.Viewport:X8} Parent=0x{ids.Middle:X8} SizeRef={middle},{top} CentralNode=1");
        result.AppendLine($"      DockNode ID=0x{ids.Bottom:X8} Parent=0x{ids.Middle:X8} SizeRef={middle},{bottom}");
        result.AppendLine($"    DockNode ID=0x{ids.Right:X8} Parent=0x{ids.Rest:X8} SizeRef={right},{height}");
        return result.ToString();

        void Window(string name, uint node, int order = 0)
        {
            result.AppendLine($"[Window][{name}]").AppendLine("Pos=0,0").AppendLine("Size=320,240")
                .AppendLine("Collapsed=0").AppendLine($"DockId=0x{node:X8},{order}").AppendLine();
        }
    }
}
