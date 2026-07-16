namespace Karpik.Editor;

public sealed record ProjectRuntimeDescriptor(
    string EngineRoot,
    string ClientBundlePath,
    string ServerBundlePath,
    string ClientRunnerPath,
    string ServerRunnerPath);

public sealed record ProjectOpenResult
{
    private ProjectOpenResult(
        bool isSuccess,
        ActiveProjectContext? candidate,
        IReadOnlyList<string> diagnostics)
    {
        IsSuccess = isSuccess;
        Candidate = candidate;
        Diagnostics = diagnostics;
    }

    public bool IsSuccess { get; }
    public ActiveProjectContext? Candidate { get; }
    public IReadOnlyList<string> Diagnostics { get; }

    public static ProjectOpenResult Success(ActiveProjectContext candidate)
    {
        ArgumentNullException.ThrowIfNull(candidate);
        return new ProjectOpenResult(true, candidate, []);
    }

    public static ProjectOpenResult Failure(params string[] diagnostics) =>
        Failure((IReadOnlyList<string>)diagnostics);

    public static ProjectOpenResult Failure(IReadOnlyList<string> diagnostics)
    {
        ArgumentNullException.ThrowIfNull(diagnostics);
        if (diagnostics.Count == 0)
        {
            throw new ArgumentException("A failed project open must contain a diagnostic.", nameof(diagnostics));
        }
        return new ProjectOpenResult(false, null, diagnostics.ToArray());
    }
}
