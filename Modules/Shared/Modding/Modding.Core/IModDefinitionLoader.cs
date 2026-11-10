using Karpik.Jobs;

namespace Karpik.Engine.Shared.Modding;

public interface IModDefinitionLoader
{
    public JobHandle<Optional<ModDefinition, string>> Load(string modDirectory);
}