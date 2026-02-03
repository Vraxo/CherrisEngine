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
        // Name is typically "SkyboxName" -> looks for "SkyboxName_right" etc.
        // We need paths. ProjectFiles.Find returns paths for Editor, but Open returns stream.
        // Here we just need to try opening "name_suffix".

        var images = new ImageResult[6];

        try
        {
            StbImage.stbi_set_flip_vertically_on_load(0);

            for (int i = 0; i < 6; i++)
            {
                string faceName = name + FaceSuffixes[i];
                // We fuzzy search via Open since we don't know the extension
                using var stream = ProjectFiles.Open(faceName);

                if (stream is null)
                {
                    Console.WriteLine($"[SkyboxLoader] Could not find face '{faceName}'.");
                    return null;
                }

                images[i] = ImageResult.FromStream(stream, ColorComponents.RedGreenBlueAlpha);
            }

            if (!ValidateImageDimensions(images))
            {
                return null;
            }

            Texture cubemapTexture = CreateCubemapTexture(gd, images);
            return new(cubemapTexture, name);
        }
        catch (Exception e)
        {
            Console.WriteLine($"[SkyboxLoader] Error loading skybox: {e.Message}");
            return null;
        }
        finally
        {
            StbImage.stbi_set_flip_vertically_on_load(1);
        }
    }

    private static bool ValidateImageDimensions(ImageResult[] images)
    {
        if (images[0].Width != images[0].Height)
        {
            return false;
        }

        if (images.Any(img => img.Width != images[0].Width || img.Height != images[0].Height))
        {
            return false;
        }

        return true;
    }

    private static Texture CreateCubemapTexture(GraphicsDevice gd, ImageResult[] images)
    {
        // (Unchanged implementation)
        ImageResult firstImage = images[0];
        ResourceFactory factory = gd.ResourceFactory;

        TextureDescription textureDescription = TextureDescription.Texture2D(
            (uint)firstImage.Width, (uint)firstImage.Height, 1, (uint)images.Length,
            PixelFormat.R8_G8_B8_A8_UNorm, TextureUsage.Cubemap | TextureUsage.Sampled);

        Veldrid.Texture cubemap = factory.CreateTexture(textureDescription);

        for (uint i = 0; i < images.Length; i++)
        {
            gd.UpdateTexture(cubemap, images[i].Data, 0, 0, 0, (uint)images[i].Width, (uint)images[i].Height, 1, 0, i);
        }

        TextureView textureView = factory.CreateTextureView(new TextureViewDescription(cubemap));
        return new(cubemap, textureView);
    }
}