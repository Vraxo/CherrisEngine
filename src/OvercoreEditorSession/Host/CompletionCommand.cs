namespace OvercoreEditorSession.Host;

/// <summary>Runs a command and notifies the caller once its execution has finished on the render thread.</summary>
public sealed class CompletionCommand(IEditorCommand command, Action completed) : IEditorCommand
{
    /// <inheritdoc />
    public string Describe => command.Describe;

    /// <inheritdoc />
    public string? Execute(EditorCommandContext context)
    {
        try
        {
            return command.Execute(context);
        }
        finally
        {
            completed();
        }
    }
}
