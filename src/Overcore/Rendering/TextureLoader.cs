using Overcore.Core;
using Veldrid;

namespace Overcore;

public static class TextureLoader
{
    public static Texture LoadTextureFromFile(GraphicsDevice gd, string path)
    {
        string assetPath = !Path.IsPathRooted(path) && File.Exists(path) ? Path.GetFullPath(path) : path;
        ImageData? image = ImageLoader.LoadFromFile(assetPath);
        if (image is null)
        {
            Console.WriteLine($"[TextureLoader] Error: Texture file not found or could not be decoded at '{path}'");
            return null;
        }

        ResourceFactory factory = gd.ResourceFactory;
        uint mipLevels = (uint)Math.Floor(Math.Log(Math.Max(image.Value.Width, image.Value.Height), 2)) + 1;

        Veldrid.Texture veldridTexture = factory.CreateTexture(TextureDescription.Texture2D(
            (uint)image.Value.Width,
            (uint)image.Value.Height,
            mipLevels,
            1,
            PixelFormat.R8_G8_B8_A8_UNorm,
            TextureUsage.Sampled | TextureUsage.GenerateMipmaps));

        gd.UpdateTexture(
            veldridTexture,
            image.Value.Data,
            0, 0, 0,
            (uint)image.Value.Width, (uint)image.Value.Height, 1,
            0, 0);

        CommandList cl = factory.CreateCommandList();
        cl.Begin();
        cl.GenerateMipmaps(veldridTexture);
        cl.End();
        gd.SubmitCommands(cl);
        gd.WaitForIdle();
        cl.Dispose();

        TextureView textureView = factory.CreateTextureView(veldridTexture);
        return new Texture(veldridTexture, textureView);
    }
}
