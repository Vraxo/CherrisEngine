using Cherris.Components;
using OpenTK.Audio.OpenAL;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.Numerics;

namespace Cherris.Core;

public class AudioSystem : IDisposable
{
    private ALDevice _device;
    private ALContext _context;

    private const int MaxAudioSources = 32;
    private readonly int[] _alSources;
    private readonly ConcurrentQueue<int> _availableSources;
    private readonly ConcurrentDictionary<AudioSource, int> _activeSources;

    public AudioSystem()
    {
        _alSources = new int[MaxAudioSources];
        _availableSources = new();
        _activeSources = new();
    }

    public void Initialize()
    {
        try
        {
            _device = ALC.OpenDevice(null);
            _context = ALC.CreateContext(_device, null as int[]);
            
            ALC.MakeContextCurrent(_context);
            CheckAlError("Initialize - MakeContextCurrent");

            AL.GenSources(_alSources);
            CheckAlError("Initialize - GenSources");

            foreach (int source in _alSources)
            {
                _availableSources.Enqueue(source);
            }

            Console.WriteLine($"[AudioSystem] Initialized with OpenAL '{AL.Get(ALGetString.Version)}' on '{AL.Get(ALGetString.Renderer)}'");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[AudioSystem] FATAL: Could not initialize OpenAL. Audio will be disabled. Reason: {ex.Message}");
        }
    }

    public void Update(Scene scene)
    {
        if (_device.Handle == IntPtr.Zero)
        {
            return;
        }

        if (scene?.MainCamera?.GameObject.GetComponent<AudioListener>() is not null)
        {
            Transform listenerTransform = scene.MainCamera.GameObject.Transform;
            Vector3 pos = listenerTransform.Position;
            Vector3 forwardNumerics = listenerTransform.Forward;
            Vector3 upNumerics = Vector3.Transform(Vector3.UnitY, listenerTransform.Rotation);

            // Convert to OpenTK vectors for the AL call
            global::OpenTK.Mathematics.Vector3 forwardOtk = new(forwardNumerics.X, forwardNumerics.Y, forwardNumerics.Z);
            global::OpenTK.Mathematics.Vector3 upOtk = new(upNumerics.X, upNumerics.Y, upNumerics.Z);

            AL.Listener(ALListener3f.Position, pos.X, pos.Y, pos.Z);
            AL.Listener(ALListenerfv.Orientation, ref forwardOtk, ref upOtk);
        }

        // Check for sources that have finished playing
        foreach ((AudioSource audioSource, int alSource) in _activeSources)
        {
            AL.GetSource(alSource, ALGetSourcei.SourceState, out int state);
            
            if (state != (int)ALSourceState.Stopped || !_activeSources.TryRemove(audioSource, out int removedAlSource))
            {
                continue;
            }

            _availableSources.Enqueue(removedAlSource);
        }
    }

    public void Play(AudioSource audioSource)
    {
        if (_device.Handle == IntPtr.Zero || audioSource.Clip is null)
        {
            return;
        }

        if (audioSource.Clip.AlBufferHandle == 0)
        {
            Console.WriteLine($"[AudioSystem] Error: AudioSource on '{audioSource.GameObject.Name}' has an invalid AudioClip handle. Was it loaded before OpenAL was initialized?");
            return;
        }

        if (_activeSources.ContainsKey(audioSource))
        {
            Stop(audioSource);
        }

        if (!_availableSources.TryDequeue(out int alSource))
        {
            Console.WriteLine("[AudioSystem] Warning: No available audio sources to play clip.");
            return;
        }

        Console.WriteLine($"[AudioSystem] Playing clip on AL source #{alSource}.");
        _activeSources[audioSource] = alSource;

        AL.Source(alSource, ALSourcei.Buffer, audioSource.Clip.AlBufferHandle);
        AL.Source(alSource, ALSourcef.Gain, audioSource.Volume);
        AL.Source(alSource, ALSourcef.Pitch, audioSource.Pitch);
        AL.Source(alSource, ALSourceb.Looping, audioSource.Loop);

        // Restore 3D positioning
        AL.Source(alSource, ALSourceb.SourceRelative, false);
        Vector3 pos = audioSource.GameObject.Transform.Position;
        AL.Source(alSource, ALSource3f.Position, pos.X, pos.Y, pos.Z);

        CheckAlError($"Setup Source #{alSource}");

        AL.SourcePlay(alSource);
        CheckAlError($"Play Source #{alSource}");

        // Log the state immediately after the play command
        AL.GetSource(alSource, ALGetSourcei.SourceState, out int state);
        Console.WriteLine($"[AudioSystem] AL source #{alSource} state after play command: {(ALSourceState)state}");
    }

    public void Stop(AudioSource audioSource)
    {
        if (_device.Handle == IntPtr.Zero)
        {
            return;
        }

        if (!_activeSources.TryRemove(audioSource, out int alSource))
        {
            return;
        }

        AL.SourceStop(alSource);
        _availableSources.Enqueue(alSource);
    }


    public void Dispose()
    {
        if (_device.Handle == IntPtr.Zero)
        {
            return;
        }

        AL.DeleteSources(_alSources);
        ALC.MakeContextCurrent(ALContext.Null);
        ALC.DestroyContext(_context);
        ALC.CloseDevice(_device);

        Console.WriteLine("[AudioSystem] Disposed.");
    }

    [Conditional("DEBUG")]
    private static void CheckAlError(string context)
    {
        ALError error = AL.GetError();

        if (error == ALError.NoError)
        {
            return;
        }

        Console.WriteLine($"[AudioSystem] OpenAL Error after {context}: {AL.GetErrorString(error)}");
    }
}