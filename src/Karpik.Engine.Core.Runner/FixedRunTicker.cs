using System.Diagnostics;
using Karpik.Engine.Shared.DragonECS;
using Microsoft.Extensions.Logging;

namespace Karpik.Engine.Core.Runner;

internal class FixedRunTicker
{
    private readonly EcsFixedRunner _runner;
    private readonly ILogger<FixedRunTicker> _logger;
    private Action _fixedRun = null!;
    private bool _overloaded;
    
    private Stopwatch _stopwatch = Stopwatch.StartNew();
    private double _nextTickTime = 0;

    public FixedRunTicker(EcsFixedRunner runner, Application application, ILogger<FixedRunTicker> logger)
    {
        _runner = runner;
        _logger = logger;
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
                    if (!_overloaded)
                    {
                        _logger.LogWarning("Client fixed ticks overloaded; skipping ticks. Lag: {LagSeconds:F4}s", currentTime - _nextTickTime);
                        _overloaded = true;
                    }
                    _nextTickTime = currentTime + Application.TICK_DT;
                }
                else
                {
                    _overloaded = false;
                }
            };
        }
    }

    public void FixedRun()
    {
        _fixedRun.Invoke();
    }

    public void Destroy()
    {
        _fixedRun = null!;
    }
}
