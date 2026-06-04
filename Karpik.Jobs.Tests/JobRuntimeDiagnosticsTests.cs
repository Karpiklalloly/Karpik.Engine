using Karpik.Jobs;
using Xunit;

namespace Karpik.Jobs.Tests;

public sealed class JobRuntimeDiagnosticsTests
{
    [Fact]
    public void TrySchedule_WhenDescriptorPoolIsExhausted_IncrementsDescriptorExhaustion()
    {
        using JobScheduler scheduler = new(capacity: 1, maxPayloadByteLength: 64);
        NoOpJob job = default;

        Assert.True(scheduler.TrySchedule(in job, out ValueJobHandle first));
        Assert.False(scheduler.TrySchedule(in job, out ValueJobHandle exhausted));

        JobRuntimeDiagnostics diagnostics = scheduler.GetDiagnostics();

        Assert.True(first.IsValid);
        Assert.False(exhausted.IsValid);
        Assert.Equal(1, diagnostics.DescriptorExhaustionCount);
        Assert.Equal(0, diagnostics.WorkerQueueOverflowCount);
    }

    [Fact]
    public void TrySchedule_WhenDependencyBudgetIsExceeded_IncrementsDependencyBudgetExhaustion()
    {
        using JobScheduler scheduler = new(capacity: 2, maxPayloadByteLength: 64, maxDependenciesPerJob: 0);
        NoOpJob job = default;

        Assert.True(scheduler.TrySchedule(in job, out ValueJobHandle root));
        Assert.False(scheduler.TrySchedule(in job, stackalloc[] { root }, out ValueJobHandle dependent));

        JobRuntimeDiagnostics diagnostics = scheduler.GetDiagnostics();

        Assert.False(dependent.IsValid);
        Assert.Equal(1, diagnostics.DependencyBudgetExhaustionCount);
        Assert.Equal(0, diagnostics.DescriptorExhaustionCount);
    }

    [Fact]
    public void TryPublish_WhenWorkerQueueIsFull_IncrementsWorkerQueueOverflow()
    {
        using JobScheduler scheduler = new(
            capacity: 2,
            maxPayloadByteLength: 64,
            workerCount: 1,
            workerQueueCapacity: 1);
        NoOpJob job = default;

        Assert.True(scheduler.TrySchedule(in job, out ValueJobHandle first));
        Assert.True(scheduler.TrySchedule(in job, out ValueJobHandle second));
        Assert.True(scheduler.TryPublish(first, workerIndex: 0));
        Assert.False(scheduler.TryPublish(second, workerIndex: 0));

        JobRuntimeDiagnostics diagnostics = scheduler.GetDiagnostics();

        Assert.Equal(1, diagnostics.WorkerQueueOverflowCount);
        Assert.Equal(0, diagnostics.DescriptorExhaustionCount);
    }

    [Fact]
    public void SchedulePublishRunNext_NormalPath_DoesNotIncrementDiagnostics()
    {
        using JobScheduler scheduler = new(
            capacity: 1,
            maxPayloadByteLength: 64,
            workerCount: 1,
            workerQueueCapacity: 1);
        NoOpJob job = default;

        Assert.True(scheduler.TrySchedule(in job, out ValueJobHandle handle));
        Assert.True(scheduler.TryPublish(handle, workerIndex: 0));
        Assert.True(scheduler.TryRunNext(workerIndex: 0));

        JobRuntimeDiagnostics diagnostics = scheduler.GetDiagnostics();

        Assert.Equal(0, diagnostics.DescriptorExhaustionCount);
        Assert.Equal(0, diagnostics.DependencyBudgetExhaustionCount);
        Assert.Equal(0, diagnostics.WorkerQueueOverflowCount);
        Assert.Equal(0, diagnostics.RequeueOverflowCount);
    }

    private struct NoOpJob : IJob
    {
        public void Execute()
        {
        }
    }
}
