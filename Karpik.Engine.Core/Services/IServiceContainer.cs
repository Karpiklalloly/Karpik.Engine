namespace Karpik.Engine.Core;

public interface IServiceContainer : IServiceProvider
{
    public T? Get<T>() where T : class;
    public void Forget<T>() where T : class;
}