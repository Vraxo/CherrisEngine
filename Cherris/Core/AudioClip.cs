using OpenTK.Audio.OpenAL;

namespace Cherris.Core;

public class AudioClip : IDisposable
{
    public int AlBufferHandle { get; }
    private bool _disposed;

    public AudioClip(int alBufferHandle)
    {
        AlBufferHandle = alBufferHandle;
    }

    public void Dispose()
    {
        if (!_disposed)
        {
            AL.DeleteBuffer(AlBufferHandle);
            _disposed = true;
        }
    }
}