using OpenTK.Audio.OpenAL;
using System.Text;

namespace Cherris.Core;

public static class AudioLoader
{
    public static AudioClip LoadFromFile(string path)
    {
        try
        {
            using var fileStream = File.OpenRead(path);
            using var reader = new BinaryReader(fileStream);

            // -- RIFF Header --
            string signature = new string(reader.ReadChars(4));
            if (signature != "RIFF") throw new NotSupportedException("Specified stream is not a wave file.");

            reader.ReadInt32(); // Riff Chunk Size

            string format = new string(reader.ReadChars(4));
            if (format != "WAVE") throw new NotSupportedException("Specified stream is not a wave file.");

            // -- FORMAT Chunk --
            string formatSignature = new string(reader.ReadChars(4));
            if (formatSignature != "fmt ") throw new NotSupportedException("Specified wave file is not supported.");

            reader.ReadInt32(); // Format chunk size
            reader.ReadInt16(); // Audio Format
            short channels = reader.ReadInt16();
            int sampleRate = reader.ReadInt32();
            reader.ReadInt32(); // Byte Rate
            reader.ReadInt16(); // Block Align
            short bitsPerSample = reader.ReadInt16();

            // -- DATA Chunk --
            string dataSignature = new string(reader.ReadChars(4));
            // Handle cases where a 'JUNK' or other chunk is between 'fmt ' and 'data'
            while (dataSignature != "data")
            {
                int chunkSize = reader.ReadInt32();
                reader.BaseStream.Seek(chunkSize, SeekOrigin.Current);
                dataSignature = new string(reader.ReadChars(4));
            }

            int dataChunkSize = reader.ReadInt32();
            byte[] audioData = reader.ReadBytes(dataChunkSize);

            // -- OpenAL Buffer --
            ALFormat formatEnum = GetSoundFormat(channels, bitsPerSample);
            int alBuffer = AL.GenBuffer();
            AL.BufferData(alBuffer, formatEnum, audioData, sampleRate);

            Console.WriteLine($"[AudioLoader] Loaded '{Path.GetFileName(path)}' ({channels} ch, {bitsPerSample}-bit, {sampleRate} Hz)");
            return new AudioClip(alBuffer);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[AudioLoader] Error loading '{path}': {ex.Message}");
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