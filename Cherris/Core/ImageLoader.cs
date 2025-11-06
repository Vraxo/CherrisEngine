using StbImageSharp;

namespace Cherris.Core;

public static class ImageLoader
{
    public static ImageData? LoadFromFile(string path, bool flipVertical = true)
    {
        if (!File.Exists(path))
        {
            Console.WriteLine($"[ImageLoader] Error: Image file not found at '{path}'");
            return null;
        }

        try
        {
            int originalFlipState = StbImage.stbi__vertically_flip_on_load_global;
            StbImage.stbi_set_flip_vertically_on_load(flipVertical ? 1 : 0);

            using var stream = File.OpenRead(path);
            ImageResult image = ImageResult.FromStream(stream, ColorComponents.RedGreenBlueAlpha);

            // Always reset to original state
            StbImage.stbi_set_flip_vertically_on_load(originalFlipState);

            return new ImageData { Data = image.Data, Width = image.Width, Height = image.Height };
        }
        catch (Exception e)
        {
            Console.WriteLine($"[ImageLoader] Error loading image '{path}': {e.Message}");
            return null;
        }
    }
}