using System;
using System.IO;
using System.Linq;
using StbImageSharp;
using Veldrid;

namespace Cherris;

public static class SkyboxLoader
{
    public static Skybox LoadSkybox(GraphicsDevice gd, string name)
    {
        // The order corresponds to the cubemap array layers: +X, -X, +Y, -Y, +Z, -Z
        string[] faceSuffixes = { "_right", "_left", "_top", "_bottom", "_front", "_back" };
        string[] facePaths = new string[6];

        for (int i = 0; i < 6; i++)
        {
            var path = AssetFinder.FindTextureFile(name + faceSuffixes[i]);
            if (path == null)
            {
                Console.WriteLine($"[SkyboxLoader] Could not find face '{name}{faceSuffixes[i]}' for skybox.");
                return null;
            }
            facePaths[i] = path;
        }

        ImageResult[] faceImages = new ImageResult[6];
        try
        {
            StbImage.stbi_set_flip_vertically_on_load(0);
            for (int i = 0; i < 6; i++)
            {
                using (var stream = File.OpenRead(facePaths[i]))
                {
                    faceImages[i] = ImageResult.FromStream(stream, ColorComponents.RedGreenBlueAlpha);
                }
            }
        }
        finally
        {
            StbImage.stbi_set_flip_vertically_on_load(1);
        }

        var firstImage = faceImages[0];
        if (firstImage.Width != firstImage.Height)
        {
            Console.WriteLine($"[SkyboxLoader] Error: Skybox face textures must be square. Texture '{facePaths[0]}' has dimensions {firstImage.Width}x{firstImage.Height}.");
            return null;
        }
        if (faceImages.Any(img => img.Width != firstImage.Width || img.Height != firstImage.Height))
        {
            Console.WriteLine("[SkyboxLoader] Error: All faces of a skybox must have the same dimensions.");
            return null;
        }

        ResourceFactory factory = gd.ResourceFactory;
        Veldrid.Texture cubemap = factory.CreateTexture(TextureDescription.Texture2D(
            (uint)firstImage.Width, (uint)firstImage.Height, 1, 6,
            PixelFormat.R8_G8_B8_A8_UNorm,
            TextureUsage.Cubemap | TextureUsage.Sampled));

        for (uint i = 0; i < 6; i++)
        {
            var img = faceImages[i];
            gd.UpdateTexture(cubemap, img.Data, 0, 0, 0, (uint)img.Width, (uint)img.Height, 1, 0, i);
        }

        TextureView textureView = factory.CreateTextureView(new TextureViewDescription(cubemap));
        var texture = new Texture(cubemap, textureView);
        return new Skybox(texture);
    }
}