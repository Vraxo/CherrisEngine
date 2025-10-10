using System.Collections.Generic;
using Veldrid;

namespace Cherris;

public class ResourceManager
{
    private readonly Dictionary<string, Mesh> _meshes = new Dictionary<string, Mesh>();
    private readonly Dictionary<string, Texture> _textures = new Dictionary<string, Texture>();
    private readonly GraphicsDevice _graphicsDevice;

    public ResourceManager(GraphicsDevice gd)
    {
        _graphicsDevice = gd;
    }

    public void LoadInitialAssets()
    {
        // Meshes
        var cubeMesh = Mesh.CreateCube();
        _meshes.Add("Cube", cubeMesh);

        var planeMesh = Mesh.CreatePlane(20f);
        _meshes.Add("Plane", planeMesh);

        // Textures
        var checkerboard = CreateCheckerboardTexture("Checkerboard", 256, 16);
        _textures.Add("Checkerboard", checkerboard);

        var white = CreateWhiteTexture("White");
        _textures.Add("White", white);
    }

    public Mesh GetMesh(string name)
    {
        return _meshes.TryGetValue(name, out var mesh) ? mesh : null;
    }

    public Texture GetTexture(string name)
    {
        return _textures.TryGetValue(name, out var texture) ? texture : null;
    }

    private Texture CreateWhiteTexture(string name)
    {
        ResourceFactory factory = _graphicsDevice.ResourceFactory;
        Veldrid.Texture veldridTexture = factory.CreateTexture(TextureDescription.Texture2D(
            1, 1, 1, 1, PixelFormat.R8_G8_B8_A8_UNorm, TextureUsage.Sampled));

        byte[] pixelData = { 255, 255, 255, 255 };
        _graphicsDevice.UpdateTexture(veldridTexture, pixelData, 0, 0, 0, 1, 1, 1, 0, 0);

        TextureView textureView = factory.CreateTextureView(veldridTexture);
        return new Texture(veldridTexture, textureView);
    }

    private Texture CreateCheckerboardTexture(string name, uint size, uint squares)
    {
        ResourceFactory factory = _graphicsDevice.ResourceFactory;
        Veldrid.Texture veldridTexture = factory.CreateTexture(TextureDescription.Texture2D(
            size, size, 1, 1, PixelFormat.R8_G8_B8_A8_UNorm, TextureUsage.Sampled));

        var pixelData = new byte[size * size * 4];
        uint squareSize = size / squares;

        for (uint y = 0; y < size; y++)
        {
            for (uint x = 0; x < size; x++)
            {
                int index = (int)(y * size + x) * 4;
                uint squareX = x / squareSize;
                uint squareY = y / squareSize;

                bool isWhite = (squareX % 2 == 0) == (squareY % 2 == 0);
                byte color = isWhite ? (byte)255 : (byte)100;

                pixelData[index] = color;
                pixelData[index + 1] = color;
                pixelData[index + 2] = color;
                pixelData[index + 3] = 255;
            }
        }

        _graphicsDevice.UpdateTexture(veldridTexture, pixelData, 0, 0, 0, size, size, 1, 0, 0);

        TextureView textureView = factory.CreateTextureView(veldridTexture);
        return new Texture(veldridTexture, textureView);
    }

    public void Dispose()
    {
        foreach (var texture in _textures.Values)
        {
            texture.Dispose();
        }
    }
}