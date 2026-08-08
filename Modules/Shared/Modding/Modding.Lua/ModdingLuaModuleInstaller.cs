using Autofac;
using Karpik.Engine.Core;

namespace Karpik.Engine.Shared.Modding.Lua;

[Module(ModuleScope.Simulation)]
public class ModdingLuaModuleInstaller : IModuleInstaller
{
    public string Name => "Modding.Lua";
    
    public void OnRegisterServices(ContainerBuilder builder)
    {
    }

    public IModule? CreateModule() => new ModdingLuaModule();
}