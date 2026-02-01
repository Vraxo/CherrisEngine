namespace CherrisRuntime;

internal static class Program
{
    private static void Main(string[] args)
    {
        // In a real scenario, you might parse args for specific config overrides.
        // For now, we assume the executable is in the root of the build folder.

        var game = new RuntimeGame();
        game.Run();
    }
}