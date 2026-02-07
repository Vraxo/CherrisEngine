using ImGuiNET;
using OpenTK.Windowing.GraphicsLibraryFramework;
using System.Numerics;

namespace CherrisEditor.UI.Gui;

internal static class ImGuiInputHandler
{
    private static readonly Dictionary<Keys, ImGuiKey> KeyMap = new()
    {
        [Keys.Tab] = ImGuiKey.Tab,
        [Keys.Left] = ImGuiKey.LeftArrow,
        [Keys.Right] = ImGuiKey.RightArrow,
        [Keys.Up] = ImGuiKey.UpArrow,
        [Keys.Down] = ImGuiKey.DownArrow,
        [Keys.PageUp] = ImGuiKey.PageUp,
        [Keys.PageDown] = ImGuiKey.PageDown,
        [Keys.Home] = ImGuiKey.Home,
        [Keys.End] = ImGuiKey.End,
        [Keys.Insert] = ImGuiKey.Insert,
        [Keys.Delete] = ImGuiKey.Delete,
        [Keys.Backspace] = ImGuiKey.Backspace,
        [Keys.Space] = ImGuiKey.Space,
        [Keys.Enter] = ImGuiKey.Enter,
        [Keys.Escape] = ImGuiKey.Escape,
        [Keys.A] = ImGuiKey.A,
        [Keys.B] = ImGuiKey.B,
        [Keys.C] = ImGuiKey.C,
        [Keys.D] = ImGuiKey.D,
        [Keys.E] = ImGuiKey.E,
        [Keys.F] = ImGuiKey.F,
        [Keys.G] = ImGuiKey.G,
        [Keys.H] = ImGuiKey.H,
        [Keys.I] = ImGuiKey.I,
        [Keys.J] = ImGuiKey.J,
        [Keys.K] = ImGuiKey.K,
        [Keys.L] = ImGuiKey.L,
        [Keys.M] = ImGuiKey.M,
        [Keys.N] = ImGuiKey.N,
        [Keys.O] = ImGuiKey.O,
        [Keys.P] = ImGuiKey.P,
        [Keys.Q] = ImGuiKey.Q,
        [Keys.R] = ImGuiKey.R,
        [Keys.S] = ImGuiKey.S,
        [Keys.T] = ImGuiKey.T,
        [Keys.U] = ImGuiKey.U,
        [Keys.V] = ImGuiKey.V,
        [Keys.W] = ImGuiKey.W,
        [Keys.X] = ImGuiKey.X,
        [Keys.Y] = ImGuiKey.Y,
        [Keys.Z] = ImGuiKey.Z,
        [Keys.D0] = ImGuiKey._0,
        [Keys.D1] = ImGuiKey._1,
        [Keys.D2] = ImGuiKey._2,
        [Keys.D3] = ImGuiKey._3,
        [Keys.D4] = ImGuiKey._4,
        [Keys.D5] = ImGuiKey._5,
        [Keys.D6] = ImGuiKey._6,
        [Keys.D7] = ImGuiKey._7,
        [Keys.D8] = ImGuiKey._8,
        [Keys.D9] = ImGuiKey._9,
        [Keys.LeftShift] = ImGuiKey.LeftShift,
        [Keys.RightShift] = ImGuiKey.RightShift,
        [Keys.LeftControl] = ImGuiKey.LeftCtrl,
        [Keys.RightControl] = ImGuiKey.RightCtrl,
        [Keys.LeftAlt] = ImGuiKey.LeftAlt,
        [Keys.RightAlt] = ImGuiKey.RightAlt,
    };

    public static void KeyEvent(Keys key, bool down)
    {
        if (KeyMap.TryGetValue(key, out ImGuiKey imguiKey))
        {
            ImGui.GetIO().AddKeyEvent(imguiKey, down);
        }
    }

    public static void MouseButton(MouseButton button, bool down)
    {
        ImGui.GetIO().AddMouseButtonEvent((int)button, down);
    }

    public static void MouseScroll(Vector2 offset)
    {
        ImGui.GetIO().AddMouseWheelEvent(offset.X, offset.Y);
    }

    public static void CharacterTyped(char c)
    {
        ImGui.GetIO().AddInputCharacter(c);
    }
}