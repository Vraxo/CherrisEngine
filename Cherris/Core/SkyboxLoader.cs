using Cherris.Utils;
using StbImageSharp;

namespace Cherris.Core;

public static class SkyboxLoader
{
    // Updated to cleaner dot-notation. Order matches standard Cubemap face order.
    private static readonly string[] FaceNames = ["Right", "Left", "Top", "Bottom", "Front", "Back"];

    /// <summary>
    /// Loads a skybox from a folder path.
    /// Expects files named: {FolderName}.{Face}.{Extension}
    /// Example: Assets/Sky/Day/Day.Right.png
    /// </summary>
    public static ImageResult[]? LoadSkyboxImages(string folderPath)
    {
        var images = new ImageResult[6];

        try
        {
            // Normalize path to handle potential trailing slashes
            string cleanPath = folderPath.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            string baseName = Path.GetFileName(cleanPath);

            // StbImage state is global, set locally and reset in finally block
            StbImage.stbi_set_flip_vertically_on_load(0);

            for (int i = 0; i < 6; i++)
            {
                // Construct path: FolderPath/BaseName.Face (e.g., MySky/MySky.Right)
                // ProjectFiles.Open handles the extension lookup (.png, .jpg, etc.)
                string facePath = Path.Combine(cleanPath, $"{baseName}.{FaceNames[i]}");

                using var stream = ProjectFiles.Open(facePath);

                if (stream is null)
                {
                    Console.WriteLine($"[SkyboxLoader] Could not find face '{facePath}' (checked extensions).");
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
            Console.WriteLine($"[SkyboxLoader] Error loading skybox from '{folderPath}': {e.Message}");
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
            Console.WriteLine("[SkyboxLoader] Error: Skybox images must be square.");
            return false;
        }

        if (images.Any(img => img.Width != images[0].Width || img.Height != images[0].Height))
        {
            Console.WriteLine("[SkyboxLoader] Error: All skybox faces must have the same dimensions.");
            return false;
        }

        return true;
    }
}