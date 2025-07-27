using System.Numerics;
using DCFApixels.DragonECS;
using Karpik.Engine.Client;
using Karpik.Engine.Shared;
using Karpik.Engine.Shared.DragonECS;

namespace Karpik.Game.Modules;

public class OpenMenuOnPauseSystem : IEcsRunOnEvent<GameInitEvent>, IEcsRun
{
    private PauseMenu _menu;
    
    public void Run()
    {
        if (Time.IsPaused)
        {
            _menu?.Open();
        }
        else
        {
            _menu?.Close();
        }
    }

    public void RunOnEvent(ref GameInitEvent evt)
    {
        _menu = new PauseMenu(Vector2.Zero);
        _menu.Pivot = Vector2.Zero;
        _menu.Anchor = Anchor.StretchAll;
        _menu.Stretch = StretchMode.Both;
        UI.Root.Add(_menu);
    }
}