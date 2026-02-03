using Cherris.Core.Logging;
using Cherris.Utils;
using OpenTK.Audio.OpenAL;

namespace Cherris.Core;

public static class AudioLoader
{
    public static AudioClip? LoadFromFile(string path)
    {
        using var stream = ProjectFiles.Open(path);
        if (stream is null)
        {
            return null;
        }

        try
        {
            using var reader = new BinaryReader(stream);

            // -- RIFF Header --
            string signature = new(reader.ReadChars(4));
            if (signature != "RIFF")
            {
                throw new NotSupportedException("Specified stream is not a wave file.");
            }

            _ = reader.ReadInt32();

            string format = new(reader.ReadChars(4));
            if (format != "WAVE")
            {
                throw new NotSupportedException("Specified stream is not a wave file.");
            }

            // -- FORMAT Chunk --
            string formatSignature = new(reader.ReadChars(4));
            if (formatSignature != "fmt ")
            {
                throw new NotSupportedException("Specified wave file is not supported.");
            }

            _ = reader.ReadInt32();
            _ = reader.ReadInt16();
            short channels = reader.ReadInt16();
            int sampleRate = reader.ReadInt32();
            _ = reader.ReadInt32();
            _ = reader.ReadInt16();
            short bitsPerSample = reader.ReadInt16();

            // -- DATA Chunk --
            string dataSignature = new(reader.ReadChars(4));
            while (dataSignature != "data")
            {
                int chunkSize = reader.ReadInt32();
                _ = reader.BaseStream.Seek(chunkSize, SeekOrigin.Current);
                dataSignature = new string(reader.ReadChars(4));
            }

            int dataChunkSize = reader.ReadInt32();
            byte[] audioData = reader.ReadBytes(dataChunkSize);

            ALFormat formatEnum = GetSoundFormat(channels, bitsPerSample);
            int alBuffer = AL.GenBuffer();
            AL.BufferData(alBuffer, formatEnum, audioData, sampleRate);

            Logger.Info($"[AudioLoader] Loaded '{Path.GetFileName(path)}'");
            return new AudioClip(alBuffer);
        }
        catch (Exception ex)
        {
            Logger.Error($"[AudioLoader] Error loading '{path}': {ex.Message}");
            return null;
        }
    }

    private static ALFormat GetSoundFormat(int channels, int bits)
    {
        return channels switch
        {
            1 => bits == 8 ? ALFormat.Mono8 : ALFormat.Mono16,
            2 => bits == 8 ? ALFormat.Stereo8 : ALFormat.Stereo16,
            _ => throw new NotSupportedException("The specified sound format is not supported.")
        };
    }
}