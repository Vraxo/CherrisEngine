using Cherris.Attributes;
using Cherris.Core;

namespace Cherris.Components;

public class AudioSource : Component
{
    public string ClipName { get; set; } = string.Empty;

    [HideInInspector]
    public AudioClip? Clip { get; internal set; }

    [Range(0f, 1f, 0.01f)]
    public float Volume { get; set; } = 1.0f;

    [Range(0.1f, 3f, 0.01f)]
    public float Pitch { get; set; } = 1.0f;

    public bool Loop { get; set; } = false;
    public bool PlayOnAwake { get; set; } = true;

    private AudioSystem? _audioSystem;

    internal void Initialize(AudioSystem audioSystem)
    {
        _audioSystem = audioSystem;
    }

    internal void Start()
    {
        if (PlayOnAwake)
        {
            Play();
        }
    }

    public void Play()
    {
        if (Clip is null)
        {
            Console.WriteLine($"[AudioSource] Warning: No AudioClip assigned to '{GameObject?.Name}'.");
            return;
        }

        _audioSystem?.Play(this);
    }

    public void Stop()
    {
        _audioSystem?.Stop(this);
    }
}