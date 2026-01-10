namespace Karpik.Engine.Shared.AssetManagement.Base;

public interface IHasDependencies
{
    IEnumerable<string> GetDependencyPaths();
}