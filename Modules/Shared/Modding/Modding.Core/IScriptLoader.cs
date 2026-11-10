using Karpik.Jobs;

namespace Karpik.Engine.Shared.Modding;

public interface IScriptLoader
{
    JobHandle<IScriptRuntime> Load(ModDefinition definition, ExecutionSide side);
}