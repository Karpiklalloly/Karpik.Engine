using Karpik.Jobs;

namespace Karpik.Engine.Shared.Modding;

public interface IModsRegistry
{
    public const string ModsDirectory = "Mods";

    public JobHandle Scan(ExecutionSide side);

    public IEnumerable<ModDefinition> GetMods();
}