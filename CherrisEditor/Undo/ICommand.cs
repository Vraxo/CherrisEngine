namespace CherrisEditor.Undo;

public interface ICommand
{
    void Execute();
    void Undo();
}