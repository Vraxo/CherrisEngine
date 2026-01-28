using Cherris.Components;
using Cherris.Utils;
using StbImageSharp;
using Veldrid;

namespace Cherris.Core;

public static class SkyboxLoader
{
    private static readonly string[] FaceSuffixes = ["_right", "_left", "_top", "_bottom", "_front", "_back"];

    public static Skybox? LoadSkybox(GraphicsDevice gd, string name)
    {
        string[]? facePaths = FindFacePaths(name);

        if (facePaths is null)
        {
            return null;
        }

        ImageResult[]? faceImages = LoadFaceImages(facePaths);

        if (faceImages is null || !ValidateImageDimensions(faceImages, facePaths))
        {
            return null;
        }

        Texture cubemapTexture = CreateCubemapTexture(gd, faceImages);

        return new(cubemapTexture, name);
    }

    private static string[]? FindFacePaths(string baseName)
    {
        string[] facePaths = new string[6];

        for (int i = 0; i < FaceSuffixes.Length; i++)
        {
            string? path = ProjectFiles.Find(baseName + FaceSuffixes[i]);

            if (path is null)
            {
                Console.WriteLine($"[SkyboxLoader] Could not find face '{baseName}{FaceSuffixes[i]}' for skybox.");
                return null;
            }

            facePaths[i] = path;
        }

        return facePaths;
    }

    private static ImageResult[]? LoadFaceImages(string[] paths)
    {
        var images = new ImageResult[6];

        try
        {
            StbImage.stbi_set_flip_vertically_on_load(0); // Cubemaps do not need flipping

            for (int i = 0; i < paths.Length; i++)
            {
                using var stream = File.OpenRead(paths[i]);
                images[i] = ImageResult.FromStream(stream, ColorComponents.RedGreenBlueAlpha);
            }

            return images;
        }
        catch (Exception e)
        {
            Console.WriteLine($"[SkyboxLoader] Error loading skybox images: {e.Message}");
            return null;
        }
        finally
        {
            StbImage.stbi_set_flip_vertically_on_load(1); // Reset to default for other textures
        }
    }

    private static bool ValidateImageDimensions(ImageResult[] images, string[] paths)
    {
        ImageResult firstImage = images[0];

        if (firstImage.Width != firstImage.Height)
        {
            Console.WriteLine(
                $"[SkyboxLoader] Error: Skybox face texture is not square." +
                $"Texture '{paths[0]}' has dimensions {firstImage.Width}x{firstImage.Height}.");

            return false;
        }

        if (images.Any(img => img.Width != firstImage.Width || img.Height != firstImage.Height))
        {
            Console.WriteLine("[SkyboxLoader] Error: All faces of a skybox must have the same dimensions.");
            return false;
        }

        return true;
    }

    private static Texture CreateCubemapTexture(GraphicsDevice gd, ImageResult[] images)
    {
        ImageResult firstImage = images[0];
        ResourceFactory factory = gd.ResourceFactory;

        TextureDescription textureDescription = TextureDescription.Texture2D(
            (uint)firstImage.Width,
            (uint)firstImage.Height,
            1,
            (uint)images.Length,
            PixelFormat.R8_G8_B8_A8_UNorm,
            TextureUsage.Cubemap | TextureUsage.Sampled);

        Veldrid.Texture cubemap = factory.CreateTexture(textureDescription);

        for (uint i = 0; i < images.Length; i++)
        {
            ImageResult img = images[i];

            gd.UpdateTexture(
                cubemap,
                img.Data,
                0,
                0,
                0,
                (uint)img.Width,
                (uint)img.Height,
                1,
                0,
                i);
        }

        TextureView textureView = factory.CreateTextureView(new TextureViewDescription(cubemap));

        return new(cubemap, textureView);
    }
}