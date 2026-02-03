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
            for (int i = 0; i < count; i++)
            {
                string key = reader.ReadString();
                long offset = reader.ReadInt64();
                long size = reader.ReadInt64();
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
        // 1. Resolve absolute path of the output file to compare against scanned files
        string absoluteOutputPath = Path.GetFullPath(outputPath);

        // 2. Open the file for writing
        using var stream = File.Create(outputPath);
        using var writer = new BinaryWriter(stream, Encoding.UTF8);

        // 3. Scan files, EXCLUDING the bundle file itself and other artifacts
        var files = Directory.GetFiles(rootDirectory, "*", SearchOption.AllDirectories)
            .Where(f =>
            {
                string absPath = Path.GetFullPath(f);
                string fileName = Path.GetFileName(f);

                // Exclude the output package itself (critical fix)
                if (string.Equals(absPath, absoluteOutputPath, StringComparison.OrdinalIgnoreCase))
                {
                    return false;
                }

                // Exclude project config
                if (fileName.Equals("project.yaml", StringComparison.OrdinalIgnoreCase))
                {
                    return false;
                }

                // Exclude build artifacts / hidden folders
                if (f.Contains("\\bin\\") || f.Contains("/bin/") ||
                    f.Contains("\\obj\\") || f.Contains("/obj/") ||
                    f.Contains("\\.git\\") || f.Contains("/.git/"))
                {
                    return false;
                }

                return true;
            })
            .ToList();

        // 4. Write Header
        writer.Write(Magic.ToCharArray());
        writer.Write(Version);
        writer.Write(files.Count);

        // 5. Placeholder for Index
        long indexStart = stream.Position;

        // Write dummy index
        foreach (var file in files)
        {
            string relPath = Path.GetRelativePath(rootDirectory, file).Replace('\\', '/');
            writer.Write(relPath);
            writer.Write((long)0); // Offset placeholder
            writer.Write((long)0); // Size placeholder
        }

        // 6. Write Data Blobs
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
                // We still need an entry to match the index count, but size 0
                entries.Add((offset, 0));
            }
        }

        // 7. Rewrite Index with correct offsets
        long finalPosition = stream.Position;
        stream.Position = indexStart;

        for (int i = 0; i < files.Count; i++)
        {
            string relPath = Path.GetRelativePath(rootDirectory, files[i]).Replace('\\', '/');
            writer.Write(relPath);
            writer.Write(entries[i].Offset);
            writer.Write(entries[i].Size);
        }

        // Restore position (good practice, though stream closes here)
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

        byte[] data = new byte[entry.Size];
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