using Cherris.Utils;
using StbImageSharp;

namespace Cherris.Core;

public static class SkyboxLoader
{
    private static readonly string[] FaceSuffixes = ["_right", "_left", "_top", "_bottom", "_front", "_back"];

    // Returns an array of 6 ImageResults (Right, Left, Top, Bottom, Front, Back)
    public static ImageResult[]? LoadSkyboxImages(string baseName)
    {
        var images = new ImageResult[6];

        try
        {
            // Cubemaps usually don't need flipping, depending on the backend.
            // We load raw here; the consumer (ResourceManager) can decide flags if needed,
            // but StbImage state is global, so we set it locally and reset.
            StbImage.stbi_set_flip_vertically_on_load(0);

            for (int i = 0; i < 6; i++)
            {
                string faceName = baseName + FaceSuffixes[i];
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

            return images;
        }
        catch (Exception e)
        {
            Console.WriteLine($"[SkyboxLoader] Error loading skybox: {e.Message}");
            return null;
        }
        finally
        {
            StbImage.stbi_set_flip_vertically_on_load(1); // Restore default
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
}