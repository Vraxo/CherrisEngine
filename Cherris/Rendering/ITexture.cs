namespace Cherris.Rendering;

public interface ITexture : IDisposable
{
    object GetBackendHandle();
}
