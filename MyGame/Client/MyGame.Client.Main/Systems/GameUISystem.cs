using System.Numerics;
using DCFApixels.DragonECS;
using Karpik.Engine.Client.Graphics.Core;
using Karpik.Engine.Client.InputModule;
using Karpik.Engine.Core;
using ImGuiNET;

namespace Karpik.Engine.MyGame.Client.Main.Systems;

public class GameUISystem : IEcsRun, IEcsInit
{
    private GameUIDemo _demo = null!;
    
    [DI] private Input _input = null!;
    [DI] private IRenderer _renderer = null!;

    public void Init()
    {
        _demo = new GameUIDemo();
        _demo.Initialize(_renderer);
    }

    public void Run()
    {
        var mousePos = _input.MousePosition;
        var mouseDown = _input.IsMouseLeftButtonDown;
        
        _demo.Update(new Vector2(mousePos.X, mousePos.Y), mouseDown);
        _demo.Render();
        
        _demo.RenderImGuiDebug();
    }
}