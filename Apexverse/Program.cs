using Cherris;

namespace Apexverse;

class Program
{
    static void Main(string[] args)
    {
        Game game = new(GraphicsAPI.OpenTK);
        game.Run();
    }
}