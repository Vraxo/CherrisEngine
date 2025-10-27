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
        float toolbarHeight = ImGui.GetFrameHeightWithSpacing();
        ImGui.BeginChild("ToolbarChild", new Vector2(0, toolbarHeight), false, ImGuiWindowFlags.NoScrollbar | ImGuiWindowFlags.NoScrollWithMouse);

        var style = ImGui.GetStyle();
        float size = ImGui.GetContentRegionAvail().Y;
        float totalWidth = (size * 3) + (style.ItemSpacing.X * 2);
        ImGui.SetCursorPosX((ImGui.GetWindowWidth() * 0.5f) - (totalWidth * 0.5f));

        bool isPlaying = _editor.State == EditorState.Playing;
        bool isEditing = _editor.State == EditorState.Editing;

        IntPtr playPauseIcon = isPlaying ? _textureManager.GetTexture("Pause") : _textureManager.GetTexture("Play");
        if (ImGui.ImageButton("PlayPause", playPauseIcon, new Vector2(size, size)))
        {
            if (isPlaying) _editor.EnterPauseMode();
            else _editor.EnterPlayMode();
        }

        ImGui.SameLine();
        if (isEditing) { ImGui.PushStyleVar(ImGuiStyleVar.Alpha, style.Alpha * 0.5f); ImGui.BeginDisabled(); }
        if (ImGui.ImageButton("Stop", _textureManager.GetTexture("Stop"), new Vector2(size, size))) _editor.EnterEditMode();
        if (isEditing) { ImGui.EndDisabled(); ImGui.PopStyleVar(); }

        ImGui.SameLine();
        if (isEditing) { ImGui.PushStyleVar(ImGuiStyleVar.Alpha, style.Alpha * 0.5f); ImGui.BeginDisabled(); }
        if (ImGui.ImageButton("Restart", _textureManager.GetTexture("Restart"), new Vector2(size, size))) _editor.RestartPlayMode();
        if (isEditing) { ImGui.EndDisabled(); ImGui.PopStyleVar(); }

        ImGui.EndChild();
    }
}