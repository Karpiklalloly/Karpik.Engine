using Karpik.Engine.Core;
using Karpik.Jobs;

namespace Karpik.Engine.Shared.ECS;

public interface IEcsComponentOnLoad<T>
{
    public JobHandle<T> OnLoad(T component, IServiceContainer provider);
}