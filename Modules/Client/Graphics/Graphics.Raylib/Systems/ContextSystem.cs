using DCFApixels.DragonECS;
using Karpik.Engine.Client.Graphics.Core;
using Karpik.Engine.Core;
using Karpik.Engine.Shared.ECS;
using Raylib_cs;
using rlImGui_cs;

namespace Karpik.Engine.Client.Graphics.GRaylib.Systems;

internal class InitSystem : IEcsInit
{
    public void Init()
    {
        rlImGui.Setup();
    }
}

internal class BeginContextSystem : IEcsRun
{
    [DI] private ICamera _mainCamera;

    private Camera3D MainCamera => (_mainCamera as RaylibCamera)!.Camera;

    public void Run()
    {
        Raylib.BeginDrawing();
        Raylib.ClearBackground(Color.DarkGreen);

        rlImGui.Begin();
        Raylib.BeginMode3D(MainCamera);
    }
}

internal class PreEndContextSystem : IEcsRun
{
    public void Run()
    {
        Raylib.EndMode3D();
    }
}

internal class EndContextSystem : IEcsRun
{
    public void Run()
    {
        rlImGui.End();
        Raylib.EndDrawing();
    }
}

internal class DestroySystem : IEcsDestroy
{
    public void Destroy()
    {
        Raylib.EnableCursor();
        rlImGui.Shutdown();
        
        Raylib.CloseWindow();
    }
}