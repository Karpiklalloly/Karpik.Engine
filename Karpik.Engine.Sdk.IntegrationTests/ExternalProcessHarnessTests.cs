using System.ComponentModel;
using Xunit;

namespace Karpik.Engine.Sdk.IntegrationTests;

public sealed class ExternalProcessHarnessTests
{
    [Fact]
    public async Task Win32_kill_failure_with_unconfirmed_exit_preserves_owned_temporary_state()
    {
        var process = new FakeExternalChildProcess
        {
            KillException = new Win32Exception("simulated tree-kill failure"),
            WaitForExit = _ => Task.Delay(Timeout.InfiniteTimeSpan)
        };
        bool deleted = false;

        ExternalProcessTerminationException exception = await Assert.ThrowsAsync<ExternalProcessTerminationException>(
            () => ExternalTemporaryState.RunAsync(
                "owned-temp",
                () => ExternalProcessTermination.EnsureStoppedAsync(
                    process,
                    TimeSpan.FromSeconds(5)),
                _ => deleted = true));

        Assert.Equal(1, process.KillCount);
        Assert.IsType<Win32Exception>(exception.InnerException);
        Assert.False(deleted);
    }

    [Fact]
    public async Task Kill_failure_can_cleanup_after_bounded_wait_confirms_exit()
    {
        var process = new FakeExternalChildProcess
        {
            KillException = new Win32Exception("simulated tree-kill race")
        };
        process.WaitForExit = _ =>
        {
            process.HasExited = true;
            return Task.CompletedTask;
        };
        bool deleted = false;

        await ExternalTemporaryState.RunAsync(
            "owned-temp",
            () => ExternalProcessTermination.EnsureStoppedAsync(
                process,
                TimeSpan.FromMilliseconds(100)),
            _ => deleted = true);

        Assert.Equal(1, process.KillCount);
        Assert.True(deleted);
    }

    private sealed class FakeExternalChildProcess : IExternalChildProcess
    {
        public int Id => 1234;
        public bool HasExited { get; set; }
        public int KillCount { get; private set; }
        public Exception? KillException { get; init; }
        public Func<CancellationToken, Task> WaitForExit { get; set; } = _ => Task.CompletedTask;

        public void Kill(bool entireProcessTree)
        {
            KillCount++;
            if (KillException is not null)
            {
                throw KillException;
            }
        }

        public Task WaitForExitAsync(CancellationToken cancellationToken) => WaitForExit(cancellationToken);
    }
}
