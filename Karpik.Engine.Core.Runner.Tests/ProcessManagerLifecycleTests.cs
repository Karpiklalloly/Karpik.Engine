using System.Diagnostics;
using System.Reflection;
using System.Runtime.CompilerServices;
using Karpik.Engine.Core;
using Xunit;

public sealed class ProcessManagerLifecycleTests
{
    [Fact]
    public void ReloadGate_AllowsExactlyOneConcurrentOwner()
    {
        var manager = (ProcessManager)RuntimeHelpers.GetUninitializedObject(typeof(ProcessManager));
        MethodInfo? tryBegin = typeof(ProcessManager).GetMethod(
            "TryBeginReload",
            BindingFlags.Instance | BindingFlags.NonPublic);
        MethodInfo? end = typeof(ProcessManager).GetMethod(
            "EndReload",
            BindingFlags.Instance | BindingFlags.NonPublic);

        Assert.NotNull(tryBegin);
        Assert.NotNull(end);
        int owners = 0;
        Parallel.For(0, 256, _ =>
        {
            if ((bool)tryBegin.Invoke(manager, null)!)
            {
                Interlocked.Increment(ref owners);
            }
        });

        Assert.Equal(1, owners);
        end.Invoke(manager, null);
        Assert.False(manager.IsReloadInProgress);
    }

    [Fact]
    public async Task WaitForExitAsync_TimeoutRemovesHandlerFromCapturedProcess()
    {
        var manager = (ProcessManager)RuntimeHelpers.GetUninitializedObject(typeof(ProcessManager));
        using Process current = Process.GetProcessById(Environment.ProcessId);
        current.EnableRaisingEvents = true;
        FieldInfo worker = typeof(ProcessManager).GetField(
            "_workerProcess",
            BindingFlags.Instance | BindingFlags.NonPublic)!;
        worker.SetValue(manager, current);
        int baseline = ExitedSubscriberCount(current);

        Assert.False(await manager.WaitForExitAsync(TimeSpan.FromMilliseconds(20)));

        Assert.Equal(baseline, ExitedSubscriberCount(current));
        worker.SetValue(manager, null);
    }

    private static int ExitedSubscriberCount(Process process)
    {
        FieldInfo field = typeof(Process).GetFields(BindingFlags.Instance | BindingFlags.NonPublic)
            .Single(candidate =>
                typeof(MulticastDelegate).IsAssignableFrom(candidate.FieldType)
                && candidate.Name.Contains("Exited", StringComparison.OrdinalIgnoreCase));
        return ((MulticastDelegate?)field.GetValue(process))?.GetInvocationList().Length ?? 0;
    }
}
