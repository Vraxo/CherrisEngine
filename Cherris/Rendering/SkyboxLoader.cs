using Cherris.Core.Logging;
using Cherris.Utils;
using StbImageSharp;

namespace Cherris.Core;

public static class SkyboxLoader
{
    private static readonly string[] FaceFilenames =
    [
        "Right",
        "Left",
        "Top",
        "Bottom",
        "Front",
        "Back"
    ];

    public static ImageResult[]? LoadSkyboxImages(string directoryPath)
    {
        try
        {
            StbImage.stbi_set_flip_vertically_on_load(0);
            return LoadAndValidateFaces(directoryPath);
        }
        catch (Exception ex)
        {
            Logger.Error($"[SkyboxLoader] Failed to load skybox at '{directoryPath}': {ex.Message}");
            return null;
        }
        finally
        {
            StbImage.stbi_set_flip_vertically_on_load(1);
        }
    }

    private static ImageResult[]? LoadAndValidateFaces(string directoryPath)
    {
        ImageResult[]? images = LoadAllFaces(directoryPath);

        if (images is null)
        {
            return null;
        }

        if (!AreDimensionsValid(images))
        {
            Logger.Error(
                $"[SkyboxLoader] Invalid skybox dimensions in '{directoryPath}'." +
                $"All faces must be square and identical in size.");

            return null;
        }

        return images;
    }

    private static ImageResult[]? LoadAllFaces(string directoryPath)
    {
        var images = new ImageResult[6];
        string cleanPath = directoryPath.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);

        for (int i = 0; i < 6; i++)
        {
            ImageResult? image = LoadSingleFace(cleanPath, FaceFilenames[i]);

            if (image is null)
            {
                return null;
            }

            images[i] = image;
        }

        return images;
    }

    private static ImageResult? LoadSingleFace(string directory, string filename)
    {
        string path = Path.Combine(directory, filename);
        using Stream? stream = ProjectFiles.Open(path);

        if (stream is null)
        {
            Logger.Error($"[SkyboxLoader] Failed to load face '{filename}' in directory '{directory}'.");
            return null;
        }

        return ImageResult.FromStream(stream, ColorComponents.RedGreenBlueAlpha);
    }

    private static bool AreDimensionsValid(ImageResult[] images)
    {
        int width = images[0].Width;
        int height = images[0].Height;

        if (width != height)
        {
            return false;
        }

        return images.All(img => img.Width == width && img.Height == height);
    }
}