using Karpik.Engine.Shared.AssetManagement.Core;
using Karpik.Jobs;

namespace Karpik.Engine.Shared.ECS;

public interface IEcsComponentOnLoad<T>
{
    public JobHandle<T> OnLoad(T component, IAssetsManager manager);
}