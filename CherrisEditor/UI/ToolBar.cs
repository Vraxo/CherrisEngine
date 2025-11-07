using ImGuiNET;
using System.Numerics;

namespace CherrisEditor.UI;

public class Toolbar
{
    private readonly Editor _editor;
    private readonly EditorTextureManager _textureManager;

    public Toolbar(Editor editor, EditorTextureManager textureManager)
    {
        _editor = editor;
        _textureManager = textureManager;
    }

    public void Draw()
    {
        if (!BeginToolbar())
        {
            return;
        }

        DrawToolbarContent();
        ImGui.EndChild();
    }

    private static bool BeginToolbar()
    {
        ImGui.PushStyleVar(ImGuiStyleVar.WindowPadding, Vector2.Zero);

        float toolbarHeight = ImGui.GetFrameHeightWithSpacing();

        bool isVisible = ImGui.BeginChild(
            "ToolbarChild",
            new(0, toolbarHeight),
            false,
            ImGuiWindowFlags.NoScrollbar | ImGuiWindowFlags.NoScrollWithMouse);

        ImGui.PopStyleVar();

        return isVisible;
    }

    private void DrawToolbarContent()
    {
        ImGuiStylePtr style = ImGui.GetStyle();
        float availableHeight = ImGui.GetContentRegionAvail().Y;

        const float buttonVerticalMargin = 4.0f;
        float iconSize = availableHeight - (style.FramePadding.Y * 2) - buttonVerticalMargin;
        Vector2 buttonIconSize = new(x: iconSize, iconSize);

        PositionButtonsInCenter(iconSize, style);

        DrawPlayPauseButton(buttonIconSize);
        ImGui.SameLine();
        DrawStopButton(buttonIconSize, style);
        ImGui.SameLine();
        DrawRestartButton(buttonIconSize, style);
    }

    private static void PositionButtonsInCenter(float iconSize, ImGuiStylePtr style)
    {
        const int buttonCount = 3;
        float buttonsTotalWidth = (iconSize * buttonCount) + (style.ItemSpacing.X * (buttonCount - 1));
        float horizontalCenteringOffset = (ImGui.GetWindowWidth() - buttonsTotalWidth) / 2.0f;

        float buttonHeightWithPadding = iconSize + (style.FramePadding.Y * 2);
        float verticalCenteringOffset = (ImGui.GetContentRegionAvail().Y - buttonHeightWithPadding) / 2.0f;

        ImGui.SetCursorPosX(ImGui.GetCursorPosX() + horizontalCenteringOffset);
        ImGui.SetCursorPosY(ImGui.GetCursorPosY() + verticalCenteringOffset);
    }

    private void DrawPlayPauseButton(Vector2 buttonIconSize)
    {
        bool isPlaying = _editor.State == EditorState.Playing;

        IntPtr playPauseIcon = isPlaying 
            ? _textureManager.GetTexture("Pause") 
            : _textureManager.GetTexture("Play");

        if (!ImGui.ImageButton("PlayPause", playPauseIcon, buttonIconSize))
        {
            return;
        }

        if (isPlaying)
        {
            _editor.EnterPauseMode();
        }
        else
        {
            _editor.EnterPlayMode();
        }
    }

    private void DrawStopButton(Vector2 buttonIconSize, ImGuiStylePtr style)
    {
        bool isEditing = _editor.State == EditorState.Editing;

        if (isEditing)
        {
            ImGui.PushStyleVar(ImGuiStyleVar.Alpha, style.Alpha * 0.5f);
            ImGui.BeginDisabled();
        }

        if (ImGui.ImageButton("Stop", _textureManager.GetTexture("Stop"), buttonIconSize))
        {
            _editor.EnterEditMode();
        }

        if (!isEditing)
        {
            return;
        }

        ImGui.EndDisabled();
        ImGui.PopStyleVar();
    }

    private void DrawRestartButton(Vector2 buttonIconSize, ImGuiStylePtr style)
    {
        bool isEditing = _editor.State == EditorState.Editing;
        
        if (isEditing)
        {
            ImGui.PushStyleVar(ImGuiStyleVar.Alpha, style.Alpha * 0.5f);
            ImGui.BeginDisabled();
        }

        if (ImGui.ImageButton("Reset", _textureManager.GetTexture("Reset"), buttonIconSize))
        {
            _editor.RestartPlayMode();
        }

        if (isEditing)
        {
            ImGui.EndDisabled();
            ImGui.PopStyleVar();
        }
    }
}