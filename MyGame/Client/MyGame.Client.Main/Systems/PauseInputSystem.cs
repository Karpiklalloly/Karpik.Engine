using DCFApixels.DragonECS;
using Karpik.Engine.Client.InputModule;
using Karpik.Engine.Core;
using Karpik.Engine.Shared.ECS.Scheduling;
using Veldrid;

namespace Karpik.Engine.MyGame.Client.Main.Systems;

[SequentialSystem]
public sealed class PauseInputSystem : ISystemUpdate
{
    [DI] private Input _input = null!;
    [DI] private Time _time = null!;

    public void Update()
    {
        if (_input.IsPressed(Key.Escape))
        {
            _time.IsPaused = !_time.IsPaused;
        }
    }
}
