using StbImageSharp;
using System;
using System.IO;
using Veldrid;

namespace Cherris;

public static class TextureLoader
{
    public static Texture LoadTextureFromFile(GraphicsDevice gd, string path)
    {
        if (!File.Exists(path))
        {
            Console.WriteLine($"[TextureLoader] Error: Texture file not found at '{path}'");
            return null;
        }

        ImageResult imageResult;
        try
        {
            // Load the image from file and flip it vertically.
            // Most image formats store data top-to-bottom, but GPUs expect it bottom-to-top.
            StbImage.stbi_set_flip_vertically_on_load(1);
            using (var stream = File.OpenRead(path))
            {
                imageResult = ImageResult.FromStream(stream, ColorComponents.RedGreenBlueAlpha);
            }
        }
        catch (Exception e)
        {
            Console.WriteLine($"[TextureLoader] Error loading texture: {e.Message}");
            return null;
        }

        ResourceFactory factory = gd.ResourceFactory;
        Veldrid.Texture veldridTexture = factory.CreateTexture(TextureDescription.Texture2D(
            (uint)imageResult.Width,
            (uint)imageResult.Height,
            1,
            1,
            PixelFormat.R8_G8_B8_A8_UNorm,
            TextureUsage.Sampled));

        // Copy the pixel data to the Veldrid texture.
        gd.UpdateTexture(
            veldridTexture,
            imageResult.Data,
            0, 0, 0,
            (uint)imageResult.Width, (uint)imageResult.Height, 1,
            0, 0);

        TextureView textureView = factory.CreateTextureView(veldridTexture);
        return new Texture(veldridTexture, textureView);
    }
}