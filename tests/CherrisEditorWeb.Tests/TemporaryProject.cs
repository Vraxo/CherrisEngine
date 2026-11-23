namespace CherrisEditorWeb.Tests;

/// <summary>A throwaway project folder for tests that write files, removed when the test ends.</summary>
internal sealed class TemporaryProject : IDisposable
{
    public TemporaryProject()
    {
        Root = Path.Combine(Path.GetTempPath(), "cherris-test-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Root);
    }

    /// <summary>Absolute folder standing in for a project root.</summary>
    public string Root { get; }

    public void Dispose()
    {
        try
        {
            Directory.Delete(Root, recursive: true);
        }
        catch (IOException)
        {
            // A leftover folder in the temp directory is not worth failing a test over.
        }
    }
}
