using System;
using Cherris.Rendering;

namespace Cherris;

public class OpenTKBackend : IGraphicsBackend
{
    public IGameWindow GameWindow => throw new NotImplementedException();
    public IRenderer Renderer => throw new NotImplementedException();
    public IResourceManager ResourceManager => throw new NotImplementedException();

    public void Initialize(string windowTitle, int width, int height, bool startWithMouseLocked)
    {
        throw new NotImplementedException("OpenTK backend is not yet implemented.");
    }

    public void Dispose()
    {
    }
}