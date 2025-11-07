using Cherris.Components;
using Cherris.Core;

namespace Cherris.Core;

/// <summary>
/// Manages loading and playback of audio in the scene.
/// This is a placeholder implementation. A real engine would use a library like OpenAL or FAudio.
/// </summary>
public class AudioSystem : IDisposable
{
    public void Initialize()
    {
        Console.WriteLine("[AudioSystem] Initialized.");
    }

    public void Update(Scene scene)
    {
        if (scene?.MainCamera?.GameObject.GetComponent<AudioListener>() is not null)
        {
            var listenerTransform = scene.MainCamera.GameObject.Transform;
            // In a real implementation:
            // Backend.SetListenerPosition(listenerTransform.Position);
            // Backend.SetListenerOrientation(listenerTransform.Forward, listenerTransform.Up);
        }

        // In a real implementation, this would update positions of active AudioSources.
    }

    public void Dispose()
    {
        Console.WriteLine("[AudioSystem] Disposed.");
    }
}