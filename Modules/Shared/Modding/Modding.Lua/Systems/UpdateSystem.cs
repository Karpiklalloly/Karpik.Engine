using Karpik.Engine.Core;
using Karpik.Engine.Shared.AssetManagement.Core;

namespace Karpik.Engine.Shared.Modding.Lua.Systems;

internal sealed class InitSystem(
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

internal class UpdateSystem(IModManager modManager) : ISystemLateUpdate
{
    public void LateUpdate()
    {
        modManager.UpdateMods();
    }
}