using Karpik.Engine.Core;

namespace Karpik.Editor;

public sealed class ProjectRuntimeResolver
{
    private readonly ProjectRuntimeDescriptor _runtime;

    public ProjectRuntimeResolver(ProjectRuntimeDescriptor runtime)
    {
        ArgumentNullException.ThrowIfNull(runtime);
        _runtime = runtime;
    }

    public EditorRuntimeDescriptor Resolve(Side side)
    {
        return side switch
        {
            Side.Client => new EditorRuntimeDescriptor(
                side,
                _runtime.ClientBundlePath,
                _runtime.ClientRunnerPath),
            Side.Server => new EditorRuntimeDescriptor(
                side,
                _runtime.ServerBundlePath,
                _runtime.ServerRunnerPath),
            _ => throw new ArgumentOutOfRangeException(nameof(side), side, "Only Client and Server sides are supported.")
        };
    }

    public sealed record EditorRuntimeDescriptor(
        Side Side,
        string BundlePath,
        string RunnerPath);
}