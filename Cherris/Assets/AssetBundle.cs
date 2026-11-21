using Cherris.Core.Logging;
using System.Text;

namespace Cherris.Core;

public class AssetBundle : IDisposable
{
    private const string Magic = "CHPK";
    private const int Version = 1;
    private FileStream? _stream;
    private readonly Dictionary<string, (long Offset, long Size)> _entries = new(StringComparer.OrdinalIgnoreCase);

    public static AssetBundle? Load(string path)
    {
        if (!File.Exists(path))
        {
            return null;
        }

        var bundle = new AssetBundle();
        try
        {
            bundle._stream = File.OpenRead(path);
            using var reader = new BinaryReader(bundle._stream, Encoding.UTF8, true);

            string magic = new(reader.ReadChars(4));
            if (magic != Magic)
            {
                throw new Exception("Invalid bundle signature");
            }

            int version = reader.ReadInt32();
            if (version != Version)
            {
                throw new Exception($"Unsupported version {version}");
            }

            int count = reader.ReadInt32();
            if (count is < 0 or > 1_000_000)
            {
                throw new Exception($"Invalid file count: {count}");
            }

            for (int i = 0; i < count; i++)
            {
                string key = reader.ReadString();
                long offset = reader.ReadInt64();
                long size = reader.ReadInt64();

                if (size < 0)
                {
                    throw new Exception($"Invalid size for asset '{key}'");
                }

                bundle._entries[key] = (offset, size);
            }

            return bundle;
        }
        catch (Exception ex)
        {
            Logger.Error($"[AssetBundle] Failed to load bundle: {ex.Message}");
            bundle.Dispose();
            return null;
        }
    }

    public static void Create(string outputPath, string rootDirectory)
    {
        string absoluteOutputPath = Path.GetFullPath(outputPath);

        using var stream = File.Create(outputPath);
        using var writer = new BinaryWriter(stream, Encoding.UTF8);

        var files = Directory.GetFiles(rootDirectory, "*", SearchOption.AllDirectories)
            .Where(f =>
            {
                string absPath = Path.GetFullPath(f);
                string fileName = Path.GetFileName(f);

                if (string.Equals(absPath, absoluteOutputPath, StringComparison.OrdinalIgnoreCase))
                {
                    return false;
                }

                if (fileName.Equals("project.yaml", StringComparison.OrdinalIgnoreCase))
                {
                    return false;
                }

                if (f.Contains(Path.DirectorySeparatorChar + "bin" + Path.DirectorySeparatorChar) ||
                    f.Contains(Path.DirectorySeparatorChar + "obj" + Path.DirectorySeparatorChar) ||
                    f.Contains(".git"))
                {
                    return false;
                }

                return true;
            })
            .ToList();

        writer.Write(Magic.ToCharArray());
        writer.Write(Version);
        writer.Write(files.Count);

        long indexStart = stream.Position;

        foreach (var file in files)
        {
            string relPath = Path.GetRelativePath(rootDirectory, file).Replace('\\', '/');
            writer.Write(relPath);
            writer.Write((long)0);
            writer.Write((long)0);
        }

        var entries = new List<(long Offset, long Size)>();

        foreach (var file in files)
        {
            long offset = stream.Position;
            try
            {
                using var fs = File.OpenRead(file);
                fs.CopyTo(stream);
                entries.Add((offset, fs.Length));
            }
            catch (Exception ex)
            {
                Logger.Warning($"[AssetBundle] Failed to pack '{file}': {ex.Message}");
                entries.Add((offset, 0));
            }
        }

        long finalPosition = stream.Position;
        stream.Position = indexStart;

        for (int i = 0; i < files.Count; i++)
        {
            string relPath = Path.GetRelativePath(rootDirectory, files[i]).Replace('\\', '/');
            writer.Write(relPath);
            writer.Write(entries[i].Offset);
            writer.Write(entries[i].Size);
        }

        stream.Position = finalPosition;
    }

    public bool HasFile(string path)
    {
        return _entries.ContainsKey(Normalize(path));
    }

    public Stream? Open(string path)
    {
        if (!_entries.TryGetValue(Normalize(path), out var entry))
        {
            return null;
        }

        if (entry.Size > int.MaxValue)
        {
            Logger.Error($"[AssetBundle] Asset '{path}' is too large ({entry.Size} bytes) to load into memory.");
            return null;
        }

        byte[] data = new byte[(int)entry.Size];
        lock (_stream!)
        {
            _stream.Seek(entry.Offset, SeekOrigin.Begin);
            _stream.ReadExactly(data, 0, data.Length);
        }
        return new MemoryStream(data);
    }

    private static string Normalize(string path)
    {
        return path.Replace('\\', '/');
    }

    public void Dispose()
    {
        _stream?.Dispose();
    }
}