using Karpik.Jobs;

namespace Karpik.Engine.Shared.Modding;

public interface IModsRegistry
{
    public const string ModsDirectory = "Mods";

    public JobHandle Scan(ExecutionSide side, CancellationToken cancellationToken = default);

    public IEnumerable<ModDefinition> GetMods();
}
