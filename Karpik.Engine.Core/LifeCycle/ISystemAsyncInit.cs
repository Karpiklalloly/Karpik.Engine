namespace Karpik.Engine.Core;

public interface ISystemAsyncInit : ISystem
{
    ValueTask InitAsync(CancellationToken cancellationToken);
}
