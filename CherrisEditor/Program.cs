using Cherris;
using CherrisEditor;

class Program
{
    static void Main(string[] args)
    {
        Editor editor = new(GraphicsAPI.OpenTK);
        editor.Run();
    }
}