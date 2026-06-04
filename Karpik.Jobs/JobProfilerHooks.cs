namespace Karpik.Jobs;

public sealed class JobProfilerHooks
{
    public JobProfilerCallback? Published { get; init; }
    public JobProfilerCallback? Started { get; init; }
    public JobProfilerCallback? Completed { get; init; }
    public JobProfilerCallback? Failed { get; init; }
}
