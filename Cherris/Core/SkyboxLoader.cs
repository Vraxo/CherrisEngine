using Cherris.Core.Logging;
using Cherris.Utils;
using StbImageSharp;

namespace Cherris.Core;

public static class SkyboxLoader
{
    // Updated to cleaner dot-notation. Order matches standard Cubemap face order.
    private static readonly string[] FaceNames = ["Right", "Left", "Top", "Bottom", "Front", "Back"];

    // Explicitly probe these extensions because "Name.Right" looks like a file with extension ".Right"
    // to the file system, causing ProjectFiles.Find's fuzzy search to fail.
    private static readonly string[] Extensions = [".png", ".jpg", ".jpeg", ".tga", ".bmp"];

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
                // Construct base path: FolderPath/BaseName.Face (e.g., MySky/MySky.Right)
                string baseFacePath = Path.Combine(cleanPath, $"{baseName}.{FaceNames[i]}");
                Stream? stream = null;

                // Probe for extensions manually
                foreach (string ext in Extensions)
                {
                    stream = ProjectFiles.Open(baseFacePath + ext);
                    if (stream is not null)
                    {
                        break;
                    }
                }

                if (stream is null)
                {
                    Logger.Error($"[SkyboxLoader] Failed to load skybox '{folderPath}'. Could not find face '{baseFacePath}' with any supported extension ({string.Join(", ", Extensions)}).");
                    return null;
                }

                using (stream)
                {
                    images[i] = ImageResult.FromStream(stream, ColorComponents.RedGreenBlueAlpha);
                }
            }

            if (!ValidateImageDimensions(images, folderPath))
            {
                return null;
            }

            return images;
        }
        catch (Exception e)
        {
            Logger.Error($"[SkyboxLoader] Exception loading skybox from '{folderPath}': {e.Message}");
            return null;
        }
        finally
        {
            StbImage.stbi_set_flip_vertically_on_load(1); // Restore default
        }
    }

    private static bool ValidateImageDimensions(ImageResult[] images, string folderPath)
    {
        if (images[0].Width != images[0].Height)
        {
            Logger.Error($"[SkyboxLoader] Invalid dimensions for '{folderPath}': Skybox images must be square (found {images[0].Width}x{images[0].Height}).");
            return false;
        }

        if (images.Any(img => img.Width != images[0].Width || img.Height != images[0].Height))
        {
            Logger.Error($"[SkyboxLoader] Mismatch dimensions for '{folderPath}': All faces must have identical dimensions.");
            return false;
        }

        return true;
    }
}