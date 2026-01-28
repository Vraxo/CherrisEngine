namespace Cherris.Components;

/// <summary>
/// Represents the position and orientation of the listener in the scene for 3D audio.
/// There should typically only be one active AudioListener in a scene, usually attached to the main camera.
/// </summary>
public class AudioListener : Component
{
    // In a real implementation, this class would be used by the AudioSystem
    // to update the listener's position in the audio backend (e.g., OpenAL).
}