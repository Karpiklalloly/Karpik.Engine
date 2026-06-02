namespace Karpik.Engine.Core;

public interface IServiceRegister
{
    public IServiceRegister Register<T>(T service) where T : class;
    public IServiceRegister Register(Type serviceType, object service);
}