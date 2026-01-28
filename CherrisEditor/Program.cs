using Cherris;
using CherrisEditor;

internal class Program
{
    private static void Main(string[] args)
    {
        Editor editor = new(GraphicsAPI.OpenTK);

        if (args.Length > 0)
        {
            editor.LoadProject(args[0]);
        }
        else
        {
            string? lastProject = ProjectPersistence.GetLastProject();
            if (lastProject is not null && Directory.Exists(lastProject))
            {
                editor.LoadProject(lastProject);
            }
        }

        editor.Run();
    }
}