namespace Karpik.Engine.Shared;

public interface IHasDependencies
{
    IEnumerable<string> GetDependencyPaths();
}