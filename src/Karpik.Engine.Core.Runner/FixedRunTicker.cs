using Karpik.Engine.Shared.DragonECS;
using Microsoft.Extensions.Logging;

namespace Karpik.Engine.Core.Runner;

internal sealed class FixedRunTicker
{
    private readonly ILogger<FixedRunTicker> _logger;
    private readonly TimeProvider _clock;
    private readonly Side _side;
    private readonly long _origin;
    private Action _runFixed;
    private long _maintenanceStarted, _excludedTicks;
    private double _nextTickTime;
    private bool _maintenance, _overloaded;

    public FixedRunTicker(EcsFixedRunner runner, Application application, ILogger<FixedRunTicker> logger)
        : this(runner.FixedRun, application.ApplicationSide, logger, TimeProvider.System) { }

    internal FixedRunTicker(Action fixedRun, Side side, ILogger<FixedRunTicker> logger, TimeProvider timeProvider)
    {
        ArgumentNullException.ThrowIfNull(fixedRun);
        ArgumentNullException.ThrowIfNull(logger);
        ArgumentNullException.ThrowIfNull(timeProvider);
        if (side is not (Side.Client or Side.Server)) throw new ArgumentOutOfRangeException(nameof(side));
        _runFixed = fixedRun;
        _side = side;
        _logger = logger;
        _clock = timeProvider;
        _origin = timeProvider.GetTimestamp();
    }

    internal void PauseForMaintenance()
    {
        if (_maintenance) return;
        _maintenanceStarted = _clock.GetTimestamp();
        _maintenance = true;
    }

    internal void ResumeAfterMaintenance()
    {
        if (!_maintenance) return;
        _excludedTicks += _clock.GetTimestamp() - _maintenanceStarted;
        _maintenance = false;
    }

    public void FixedRun()
    {
        if (_maintenance) return;
        if (_side == Side.Server)
        {
            _runFixed();
            return;
        }
        double currentTime = (double)(_clock.GetTimestamp() - _origin - _excludedTicks) / _clock.TimestampFrequency;
        int loops = 0;
        while (currentTime >= _nextTickTime && loops < 5)
        {
            _runFixed();
            _nextTickTime += Application.TICK_DT;
            loops++;
        }
        if (loops >= 5)
        {
            if (!_overloaded)
                _logger.LogWarning("Client fixed ticks overloaded; skipping ticks. Lag: {LagSeconds:F4}s", currentTime - _nextTickTime);
            _overloaded = true;
            _nextTickTime = currentTime + Application.TICK_DT;
        }
        else _overloaded = false;
    }

    public void Destroy() => _runFixed = null!;
}
