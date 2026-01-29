using Cherris.Components;
using CherrisEditor.Undo;
using CherrisEditor.Undo.Commands;
using ImGuiNET;

namespace CherrisEditor.Inspectors;

[CustomInspector(typeof(AudioSource))]
public sealed class AudioSourceInspector : IComponentInspector
{
    private readonly HistoryManager _history;

    public AudioSourceInspector(HistoryManager history)
    {
        _history = history;
    }

    public bool Draw(Component component)
    {
        var source = (AudioSource)component;
        bool dirty = false;

        if (!ImGui.BeginTable("AudioSourceTable", 2))
        {
            return false;
        }

        ImGui.TableSetupColumn("Property", ImGuiTableColumnFlags.WidthStretch);
        ImGui.TableSetupColumn("Value", ImGuiTableColumnFlags.WidthStretch);

        // Clip Name
        ImGui.TableNextRow();
        ImGui.TableSetColumnIndex(0);
        ImGui.Text("Clip Name");
        ImGui.TableSetColumnIndex(1);
        ImGui.PushItemWidth(-1);

        string clipName = source.ClipName;
        if (ImGui.InputText("##ClipName", ref clipName, 256) && ImGui.IsItemDeactivatedAfterEdit())
        {
            _history.Execute(new ChangePropertyCommand(source, typeof(AudioSource).GetProperty(nameof(AudioSource.ClipName))!, source.ClipName, clipName));
            source.ClipName = clipName;
            dirty = true;
        }
        ImGui.PopItemWidth();

        // Volume
        ImGui.TableNextRow();
        ImGui.TableSetColumnIndex(0);
        ImGui.Text("Volume");
        ImGui.TableSetColumnIndex(1);
        ImGui.PushItemWidth(-1);

        float volume = source.Volume;
        if (PropertyDrawer.Float("##Volume", ref volume, 0.01f, 0f, 1f))
        {
            source.Volume = volume;
            dirty = true;
        }
        if (ImGui.IsItemDeactivatedAfterEdit() && Math.Abs(volume - source.Volume) > 0.0001f)
        {
            // Handled by the command pattern in UndoTracker elsewhere
        }
        ImGui.PopItemWidth();

        // Pitch
        ImGui.TableNextRow();
        ImGui.TableSetColumnIndex(0);
        ImGui.Text("Pitch");
        ImGui.TableSetColumnIndex(1);
        ImGui.PushItemWidth(-1);

        float pitch = source.Pitch;
        if (PropertyDrawer.Float("##Pitch", ref pitch, 0.01f, 0.1f, 3f))
        {
            source.Pitch = pitch;
            dirty = true;
        }
        ImGui.PopItemWidth();

        // Loop
        ImGui.TableNextRow();
        ImGui.TableSetColumnIndex(0);
        ImGui.Text("Loop");
        ImGui.TableSetColumnIndex(1);

        bool loop = source.Loop;
        if (PropertyDrawer.Bool("##Loop", ref loop) && loop != source.Loop)
        {
            _history.Execute(new ChangePropertyCommand(source, typeof(AudioSource).GetProperty(nameof(AudioSource.Loop))!, source.Loop, loop));
            source.Loop = loop;
            dirty = true;
        }

        // Play On Awake
        ImGui.TableNextRow();
        ImGui.TableSetColumnIndex(0);
        ImGui.Text("Play On Awake");
        ImGui.TableSetColumnIndex(1);

        bool playOnAwake = source.PlayOnAwake;
        if (PropertyDrawer.Bool("##PlayOnAwake", ref playOnAwake) && playOnAwake != source.PlayOnAwake)
        {
            _history.Execute(new ChangePropertyCommand(source, typeof(AudioSource).GetProperty(nameof(AudioSource.PlayOnAwake))!, source.PlayOnAwake, playOnAwake));
            source.PlayOnAwake = playOnAwake;
            dirty = true;
        }

        ImGui.EndTable();
        return dirty;
    }
}