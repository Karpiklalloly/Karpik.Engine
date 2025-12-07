namespace Karpik.Engine.Shared;

public interface IEcsComponentOnLoad<T>
{
    public JobHandle<T> OnLoad(T component, AssetsManager manager);
}