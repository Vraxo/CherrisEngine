using Cherris;
using System.Diagnostics;

namespace Apexverse;

class Program
{
    static void Main(string[] args)
    {
        var totalStartupTimer = Stopwatch.StartNew();

        var game = new Game(EngineMode.Editor);
        game.Run(totalStartupTimer);
    }
}