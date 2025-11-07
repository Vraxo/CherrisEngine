using Cherris.Components;
using CherrisEditor.Undo;
using CherrisEditor.Undo.Commands;
using ImGuiNET;

namespace CherrisEditor.Inspectors;

[CustomInspector(typeof(AudioSource))]
public class AudioSourceInspector : IComponentInspector
{
    private readonly HistoryManager _history;
    private object _undoInitialValue;

    public AudioSourceInspector(HistoryManager history)
    {
        _history = history;
    }

    public bool Draw(Component component)
    {
        var audioSource = (AudioSource)component;
        bool dirty = false;

        if (!ImGui.BeginTable("AudioSourceTable", 2)) return false;
        ImGui.TableSetupColumn("Property", ImGuiTableColumnFlags.WidthStretch);
        ImGui.TableSetupColumn("Value", ImGuiTableColumnFlags.WidthStretch);

        // Clip Name
        ImGui.TableNextRow();
        ImGui.TableSetColumnIndex(0); ImGui.Text("Audio Clip");
        ImGui.TableSetColumnIndex(1);
        ImGui.PushItemWidth(-1.0f);
        string clipName = audioSource.ClipName ?? "";
        if (ImGui.InputText("##ClipName", ref clipName, 256))
        {
            if (ImGui.IsItemDeactivatedAfterEdit())
            {
                _history.Execute(new ChangePropertyCommand(audioSource, typeof(AudioSource).GetProperty(nameof(AudioSource.ClipName)), audioSource.ClipName, clipName));
            }
            dirty = true;
        }
        ImGui.PopItemWidth();

        // Volume
        ImGui.TableNextRow();
        ImGui.TableSetColumnIndex(0); ImGui.Text("Volume");
        ImGui.TableSetColumnIndex(1);
        ImGui.PushItemWidth(-1.0f);
        var volumeBeforeEdit = audioSource.Volume;
        var volume = volumeBeforeEdit;
        if (ImGui.DragFloat("##Volume", ref volume, 0.01f, 0.0f, 1.0f))
        {
            audioSource.Volume = volume; dirty = true;
        }
        HandleUndo(audioSource, nameof(AudioSource.Volume), volumeBeforeEdit, ImGui.IsItemActivated(), ImGui.IsItemDeactivatedAfterEdit());
        ImGui.PopItemWidth();

        // Pitch
        ImGui.TableNextRow();
        ImGui.TableSetColumnIndex(0); ImGui.Text("Pitch");
        ImGui.TableSetColumnIndex(1);
        ImGui.PushItemWidth(-1.0f);
        var pitchBeforeEdit = audioSource.Pitch;
        var pitch = pitchBeforeEdit;
        if (ImGui.DragFloat("##Pitch", ref pitch, 0.01f, 0.1f, 3.0f))
        {
            audioSource.Pitch = pitch; dirty = true;
        }
        HandleUndo(audioSource, nameof(AudioSource.Pitch), pitchBeforeEdit, ImGui.IsItemActivated(), ImGui.IsItemDeactivatedAfterEdit());
        ImGui.PopItemWidth();

        // Loop
        ImGui.TableNextRow();
        ImGui.TableSetColumnIndex(0); ImGui.Text("Loop");
        ImGui.TableSetColumnIndex(1);
        var loopBeforeEdit = audioSource.Loop;
        var loop = loopBeforeEdit;
        if (ImGui.Checkbox("##Loop", ref loop))
        {
            _history.Execute(new ChangePropertyCommand(audioSource, typeof(AudioSource).GetProperty(nameof(AudioSource.Loop)), loopBeforeEdit, loop));
            dirty = true;
        }

        // Play On Awake
        ImGui.TableNextRow();
        ImGui.TableSetColumnIndex(0); ImGui.Text("Play On Awake");
        ImGui.TableSetColumnIndex(1);
        var playOnAwakeBeforeEdit = audioSource.PlayOnAwake;
        var playOnAwake = playOnAwakeBeforeEdit;
        if (ImGui.Checkbox("##PlayOnAwake", ref playOnAwake))
        {
            _history.Execute(new ChangePropertyCommand(audioSource, typeof(AudioSource).GetProperty(nameof(AudioSource.PlayOnAwake)), playOnAwakeBeforeEdit, playOnAwake));
            dirty = true;
        }

        ImGui.EndTable();
        return dirty;
    }

    private void HandleUndo(object target, string propertyName, object valueBeforeEdit, bool activated, bool deactivated)
    {
        var property = target.GetType().GetProperty(propertyName);
        if (property is null) return;

        if (activated)
        {
            _undoInitialValue = valueBeforeEdit;
        }

        if (deactivated)
        {
            object valueAfterEdit = property.GetValue(target);
            if (_undoInitialValue is not null && !_undoInitialValue.Equals(valueAfterEdit))
            {
                // Revert the change so the command can apply it
                property.SetValue(target, _undoInitialValue);
                _history.Execute(new ChangePropertyCommand(target, property, _undoInitialValue, valueAfterEdit));
            }
            _undoInitialValue = null;
        }
    }
}