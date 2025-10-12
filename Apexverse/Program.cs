using Cherris;

namespace Apexverse;

class Program
{
    static void Main(string[] args)
    {
        var game = new Game(EngineMode.Game);
        game.Run();
    }
}