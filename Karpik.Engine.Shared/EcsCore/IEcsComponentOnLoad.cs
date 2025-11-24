namespace Karpik.Engine.Shared;

public interface IEcsComponentOnLoad
{
    public Task OnLoad(AssetsManager manager);
}