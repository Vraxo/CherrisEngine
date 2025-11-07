using Cherris.Components;
using CherrisEditor.Inspectors;
using ImGuiNET;

namespace CherrisEditor;

[CustomInspector(typeof(AudioListener))]
public class AudioListenerInspector : IComponentInspector
{
    public bool Draw(Component component)
    {
        ImGui.TextWrapped("Acts as the 'ears' in the 3D world for positional audio.");
        ImGui.Separator();
        ImGui.TextWrapped("There should only be one Audio Listener in a scene, typically attached to the main camera.");
        return false;
    }
}