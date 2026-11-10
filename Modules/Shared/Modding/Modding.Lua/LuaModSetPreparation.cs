using System.Composition;
using Karpik.Engine.Core;

namespace Karpik.Engine.Shared.Modding.Lua;

[Export(typeof(IModSetPreparation))]
[ServiceRegistration(ModuleScope.ModSet, ServiceLifetime.Singleton)]
public sealed class LuaModSetPreparation(IModsRegistry registry, Application application) : IModSetPreparation
{
    public async ValueTask PrepareAsync(CancellationToken cancellationToken)
    {
        ExecutionSide side = application.ApplicationSide switch
        {
            Side.Client => ExecutionSide.Client,
            Side.Server => ExecutionSide.Server,
            _ => throw new InvalidOperationException("Unsupported application side.")
        };
        await registry.Scan(side, cancellationToken);
    }
}
