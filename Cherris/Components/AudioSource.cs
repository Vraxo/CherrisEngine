using Cherris.Core;

namespace Cherris.Components;

/// <summary>
/// Plays back an AudioClip in the scene.
/// </summary>
public class AudioSource : Component
{
    public string ClipName { get; set; } = string.Empty;

    public float Volume { get; set; } = 1.0f;
    public float Pitch { get; set; } = 1.0f;
    public bool Loop { get; set; } = false;
    public bool PlayOnAwake { get; set; } = true;

    // Methods that would be called by scripts to control playback.
    // In this example, they will just log to the console.
    public void Play()
    {
        if (!string.IsNullOrEmpty(ClipName))
        {
            Console.WriteLine($"[AudioSource] Playing '{ClipName}' on '{GameObject.Name}'.");
        }
        else
        {
            Console.WriteLine($"[AudioSource] Warning: No AudioClip assigned to '{GameObject.Name}'.");
        }
    }

    public void Stop()
    {
        if (!string.IsNullOrEmpty(ClipName))
        {
            Console.WriteLine($"[AudioSource] Stopping '{ClipName}' on '{GameObject.Name}'.");
        }
    }
}