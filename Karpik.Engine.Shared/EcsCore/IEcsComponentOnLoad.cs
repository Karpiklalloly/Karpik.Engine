namespace Karpik.Engine.Shared;

public interface IEcsComponentOnLoad<T>
{
    public Task<T> OnLoad(T component, AssetsManager manager);
}