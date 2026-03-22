using System.Reflection;
using DCFApixels.DragonECS;
using ImGuiNET;
using Karpik.Engine.Client.Graphics.Core;
using Karpik.Engine.Client.InputModule;
using Karpik.Engine.Core;
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
    [DI] private ICamera _mainCamera = null!;
    [DI] private Input _input = null!;
    [DI] private Time _time = null!;
    
    private Dictionary<KeyboardKey, ImGuiKey> _dictionary = (Dictionary<KeyboardKey, ImGuiKey>)typeof(rlImGui).GetField("RaylibKeyMap", BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static).GetValue(null);
    private readonly Dictionary<KeyboardKey, bool> _previousKeyStates = new Dictionary<KeyboardKey, bool>();

    public void Run()
    {
        Raylib.BeginDrawing();
        Raylib.ClearBackground(Color.DarkGreen);

        ImGuiIOPtr io = ImGui.GetIO();

        // Update keyboard state for ImGui
        foreach (var kvp in _dictionary)
        {
            KeyboardKey rlKey = kvp.Key;
            ImGuiKey imguiKey = kvp.Value;
            
            bool isDown = _input.IsDown((KeyboardKeys)rlKey);
            
            // Get previous state, default to false (up) if not tracked yet
            bool wasDown = _previousKeyStates.TryGetValue(rlKey, out bool state) ? state : false;
            
            // Only send event if state changed
            if (isDown != wasDown)
            {
                io.AddKeyEvent(imguiKey, isDown);
                _previousKeyStates[rlKey] = isDown;
            }
        }

        // Send accumulated characters
        foreach (var c in _input.Chars)
        {
            io.AddInputCharacter(c);
        }
        
        rlImGui.Begin((float)_time.DeltaTime);
        Raylib.BeginMode3D(_mainCamera.Raylib3D);
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