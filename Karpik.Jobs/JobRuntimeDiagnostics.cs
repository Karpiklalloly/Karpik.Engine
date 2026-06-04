namespace Karpik.Jobs;

public readonly struct JobRuntimeDiagnostics
{
    internal JobRuntimeDiagnostics(
        long descriptorExhaustionCount,
        long payloadTooLargeCount,
        long dependencyBudgetExhaustionCount,
        long invalidDependencyCount,
        long workerQueueOverflowCount,
        long requeueOverflowCount)
    {
        DescriptorExhaustionCount = descriptorExhaustionCount;
        PayloadTooLargeCount = payloadTooLargeCount;
        DependencyBudgetExhaustionCount = dependencyBudgetExhaustionCount;
        InvalidDependencyCount = invalidDependencyCount;
        WorkerQueueOverflowCount = workerQueueOverflowCount;
        RequeueOverflowCount = requeueOverflowCount;
    }

    public long DescriptorExhaustionCount { get; }
    public long PayloadTooLargeCount { get; }
    public long DependencyBudgetExhaustionCount { get; }
    public long InvalidDependencyCount { get; }
    public long WorkerQueueOverflowCount { get; }
    public long RequeueOverflowCount { get; }
}
