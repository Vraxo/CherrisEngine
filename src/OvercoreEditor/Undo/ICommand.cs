namespace OvercoreEditor.Undo;

public interface ICommand
{
    void Execute();
    void Undo();
}