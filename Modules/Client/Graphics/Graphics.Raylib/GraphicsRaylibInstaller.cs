using System.Reflection;
using DCFApixels.DragonECS;
using ImGuiNET;
using Karpik.Engine.Client.Graphics.Core;
using Karpik.Engine.Core;
using Raylib_cs;
using rlImGui_cs;

namespace Karpik.Engine.Client.Graphics.GRaylib;

[Module]
public class GraphicsRaylibInstaller : IModule, IModuleConfiguratable, IModuleHotReload
{
    public string Name => "Graphics.Raylib";

    public void OnRegisterServices(IServiceRegister services)
    {
        var window = new RaylibWindow();
        services.Register<IWindow>(window);
        services.Register(window);

        var renderer = new RaylibRenderer();
        services.Register<IRenderer>(renderer);
        services.Register(renderer);

        var camera = new RaylibCamera();
        services.Register<ICamera>(camera);
        services.Register(camera);
    }

    public void OnConfigure(IServiceContainer services, out IEcsModule? module)
    {
        module = new RaylibModule();
    }

    public void OnConfigureComplete(IServiceContainer services)
    {
    }

    public byte[] OnPrepareHotReload()
    {
        // var platformIo = ImGui.GetPlatformIO();
        //
        // // Зануляем нативные указатели
        // platformIo.Platform_GetClipboardTextFn = IntPtr.Zero;
        // platformIo.Platform_SetClipboardTextFn = IntPtr.Zero;
        //
        // var type = typeof(rlImGui);
        // type.GetField("GetClipCallback", BindingFlags.Static | BindingFlags.NonPublic)?.SetValue(null, null);
        // type.GetField("SetClipCallback", BindingFlags.Static | BindingFlags.NonPublic)?.SetValue(null, null);
        return [];
    }

    public bool OnHotReload(byte[] data, IServiceContainer services)
    {
        return true;
    }
}
