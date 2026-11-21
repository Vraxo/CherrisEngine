using Cherris.Core;
using Cherris.Rendering;
using CherrisEditor.Undo;

namespace CherrisEditor.Inspectors;

public sealed class ChangeMaterialTextureCommand : ICommand
{
    private readonly Material _target;
    private readonly string _oldName;
    private readonly ITexture _oldTexture;
    private readonly string _newName;
    private readonly ITexture _newTexture;

    public ChangeMaterialTextureCommand(Material target, string oldName, ITexture oldTexture, string newName, ITexture newTexture)
    {
        _target = target;
        _oldName = oldName;
        _oldTexture = oldTexture;
        _newName = newName;
        _newTexture = newTexture;
    }

    public void Execute()
    {
        _target.TextureName = _newName;
        _target.Texture = _newTexture;
    }

    public void Undo()
    {
        _target.TextureName = _oldName;
        _target.Texture = _oldTexture;
    }
}