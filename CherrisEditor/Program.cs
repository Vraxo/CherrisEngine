using Cherris;
using CherrisEditor;

class Program
{
    static void Main(string[] args)
    {
        var editor = new Editor(GraphicsAPI.OpenTK);
        editor.Run();
    }
}