namespace Karpik.Engine.Shared.AssetManagement.Core;

public interface IHasDependencies
{
    IEnumerable<string> GetDependencyPaths();
}