using Cherris.Utils;

namespace Cherris.Core;

public static class GenericCubemapLoader
{
    private static readonly string[] FaceSuffixes = { "_right", "_left", "_top", "_bottom", "_front", "_back" };

    public static ImageData[]? LoadCubemapFaces(string baseName)
    {
        var images = new ImageData[6];
        for (int i = 0; i < FaceSuffixes.Length; i++)
        {
            string? path = ProjectFiles.Find(baseName + FaceSuffixes[i]);
            if (path is null)
            {
                Console.WriteLine($"[CubemapLoader] Could not find face '{baseName}{FaceSuffixes[i]}' for skybox.");
                return null;
            }

            var imageData = ImageLoader.LoadFromFile(path, flipVertical: false);
            if (imageData is null)
            {
                return null;
            }
            images[i] = imageData.Value;
        }
        return images;
    }
}