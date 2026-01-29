using Cherris.Core.Logging;
using System.Runtime.InteropServices;
using Veldrid;

namespace Cherris.Core;

public class Snapshotter : IDisposable
{
    private readonly GraphicsDevice _graphicsDevice;
    private Veldrid.Texture _stagingTexture;
    private bool _snapshotPending;
    private uint _width;
    private uint _height;

    public Snapshotter(GraphicsDevice graphicsDevice)
    {
        _graphicsDevice = graphicsDevice;
    }

    public void RecordCopyCommand(CommandList cl, Veldrid.Texture source)
    {
        if (source is null)
        {
            return;
        }

        // Ensure the staging texture is the correct size
        if (_stagingTexture is null || _width != source.Width || _height != source.Height)
        {
            _stagingTexture?.Dispose();
            _width = source.Width;
            _height = source.Height;
            _stagingTexture = _graphicsDevice.ResourceFactory.CreateTexture(TextureDescription.Texture2D(
                _width, _height, 1, 1, source.Format, TextureUsage.Staging));
        }

        cl.CopyTexture(source, _stagingTexture);
        _snapshotPending = true;
    }

    public void SaveCopiedData(string path)
    {
        if (!_snapshotPending || _stagingTexture is null)
        {
            return;
        }

        try
        {
            MappedResource mapped = _graphicsDevice.Map(_stagingTexture, MapMode.Read);
            try
            {
                _ = Directory.CreateDirectory(Path.GetDirectoryName(path));
                using (var stream = new FileStream(path, FileMode.Create))
                {
                    SaveAsBmp(stream, (int)_width, (int)_height, mapped);
                }
                Logger.Info($"[Snapshotter] Saved snapshot to '{path}'");
            }
            finally
            {
                _graphicsDevice.Unmap(_stagingTexture);
                _snapshotPending = false;
            }
        }
        catch (Exception e)
        {
            Logger.Error($"[Snapshotter] Error saving snapshot: {e.Message}");
        }
    }

    private static void SaveAsBmp(Stream stream, int width, int height, MappedResource map)
    {
        int rowPitch = (int)map.RowPitch;
        int bytesPerPixel = 4; // Assuming a 32-bit format like BGRA or RGBA
        int bmpRowPitch = ((width * 3) + 3) & ~3;
        int imageSize = bmpRowPitch * height;
        int fileSize = 54 + imageSize;

        using var writer = new BinaryWriter(stream);
        // File Header
        writer.Write((byte)'B');
        writer.Write((byte)'M');
        writer.Write(fileSize);
        writer.Write((ushort)0);
        writer.Write((ushort)0);
        writer.Write(54); // Offset to pixel data

        // DIB Header (BITMAPINFOHEADER)
        writer.Write(40); // Header size
        writer.Write(width);
        writer.Write(height);
        writer.Write((ushort)1); // Planes
        writer.Write((ushort)24); // Bits per pixel
        writer.Write(0); // Compression
        writer.Write(imageSize);
        writer.Write(0); // X Pixels per meter (not important)
        writer.Write(0); // Y Pixels per meter (not important)
        writer.Write(0); // Colors in palette
        writer.Write(0); // Important colors

        // Pixel Data
        byte[] rowBuffer = new byte[rowPitch];
        byte[] bmpRowBuffer = new byte[bmpRowPitch];

        // Write rows from bottom to top
        for (int y = height - 1; y >= 0; y--)
        {
            Marshal.Copy(map.Data + (y * rowPitch), rowBuffer, 0, rowPitch);
            int bmpIndex = 0;
            for (int x = 0; x < width * bytesPerPixel; x += bytesPerPixel)
            {
                // Veldrid swapchain on Windows is typically BGRA. We need to write BGR.
                bmpRowBuffer[bmpIndex++] = rowBuffer[x];     // B
                bmpRowBuffer[bmpIndex++] = rowBuffer[x + 1]; // G
                bmpRowBuffer[bmpIndex++] = rowBuffer[x + 2]; // R
            }
            writer.Write(bmpRowBuffer);
        }
    }

    public void Dispose()
    {
        _stagingTexture?.Dispose();
    }
}