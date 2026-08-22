using Karpik.Engine.Core;
using Karpik.Engine.Shared.AssetManagement.Core;

namespace Karpik.Engine.Shared.Modding.Lua.Systems;

// Public: static composition emits direct factories for module systems, so every
// ECS system must be visible from the host assembly.
public sealed class InitSystem(
    IModManager modManager,
    IAssetsManager assetsManager,
    Application application)
    : ISystemInit
{
    public void Init()
    {
        var side = application.ApplicationSide == Side.Server
            ? ExecutionSide.Server
            : ExecutionSide.Client;

        modManager.Init(side);
        modManager.LoadMods(assetsManager.ModsPath)
            .GetAwaiter()
            .GetResult();
        modManager.StartMods();
    }
}

public class UpdateSystem(IModManager modManager) : ISystemLateUpdate
{
    public void LateUpdate()
    {
        modManager.UpdateMods();
    }
}