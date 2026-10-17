namespace Karpik.Engine.Core;

public interface ISystemAsyncDestroy : ISystem
{
    ValueTask DestroyAsync();
}
