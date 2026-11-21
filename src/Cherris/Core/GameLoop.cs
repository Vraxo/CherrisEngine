using Cherris.RenderingInterface;
using System.Diagnostics;

namespace Cherris;

public class GameLoop
{
    private readonly IGameWindow _gameWindow;
    private readonly Action<float> _updateAction;
    private readonly Action _drawAction;
    private readonly Stopwatch _stopwatch;

    public GameLoop(IGameWindow gameWindow, Action<float> updateAction, Action drawAction)
    {
        _gameWindow = gameWindow;
        _updateAction = updateAction;
        _drawAction = drawAction;
        _stopwatch = new();
    }

    public void Run()
    {
        _stopwatch.Start();

        while (_gameWindow.Exists)
        {
            _gameWindow.ProcessEvents();

            if (!_gameWindow.Exists)
            {
                break;
            }

            float deltaTime = (float)_stopwatch.Elapsed.TotalSeconds;
            _stopwatch.Restart();

            _updateAction(deltaTime);
            _drawAction();
        }
    }
}