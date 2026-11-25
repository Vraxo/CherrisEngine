namespace Overcore.Rendering;

public interface ITexture : IDisposable
{
    object GetBackendHandle();
}
