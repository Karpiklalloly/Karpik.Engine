using System.Diagnostics;
using Karpik.Engine.Shared.DragonECS;

namespace Karpik.Engine.Core.Runner;

internal class FixedRunTicker
{
    private readonly EcsFixedRunner _runner;
    private Action _fixedRun = null!;
    
    private Stopwatch _stopwatch = Stopwatch.StartNew();
    private double _nextTickTime = 0;

    public FixedRunTicker(EcsFixedRunner runner, Application application)
    {
        _runner = runner;
        if (application.ApplicationSide == Side.Server)
        {
            _fixedRun = () => _runner.FixedRun();
        }
        else if (application.ApplicationSide == Side.Client)
        {
            _nextTickTime = _stopwatch.Elapsed.TotalSeconds;
            _fixedRun = () =>
            {
                double currentTime = _stopwatch.Elapsed.TotalSeconds;
                int loops = 0;
            
                while (currentTime >= _nextTickTime && loops < 5)
                {
                    _runner.FixedRun();
                    _nextTickTime += Application.TICK_DT;
                    loops++;
                }
            
                if (loops >= 5)
                {
                    Console.WriteLine($"Server overloading! Skipping ticks. Lag: {currentTime - _nextTickTime:F4}s");
                    _nextTickTime = currentTime + Application.TICK_DT;
                }
            };
        }
    }

    public void FixedRun()
    {
        _fixedRun.Invoke();
    }
}