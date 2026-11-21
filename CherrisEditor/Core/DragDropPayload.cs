using System.Runtime.InteropServices;

namespace CherrisEditor.UI;

public static class DragDropPayload
{
    public static unsafe void SendString(string type, string data)
    {
        IntPtr ptr = Marshal.StringToHGlobalAnsi(data);
        try
        {
            uint size = (uint)(data.Length + 1);
            ImGuiNET.ImGui.SetDragDropPayload(type, ptr, size);
        }
        finally
        {
            Marshal.FreeHGlobal(ptr);
        }
    }
}