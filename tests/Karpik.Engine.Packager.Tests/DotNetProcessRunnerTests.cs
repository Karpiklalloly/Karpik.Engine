using System.Diagnostics;
using Karpik.Engine.Packager;
using Xunit;

namespace Karpik.Engine.Packager.Tests;

public sealed class DotNetProcessRunnerTests
{
    [Fact]
    public void UnconfirmedTerminationThrowsTypedExceptionAfterKillAndBoundedSecondWait()
    {
        using var temporary = new PackagerTemporaryDirectory();
        var factory = new NeverExitsProcessFactory();
        var runner = new DotNetProcessRunner(
            factory,
            executionTimeout: TimeSpan.FromMilliseconds(20),
            terminationTimeout: TimeSpan.FromMilliseconds(20));

        DotNetProcessTerminationException exception = Assert.Throws<DotNetProcessTerminationException>(
            () => runner.Run(temporary.RootPath, "restore", "Synthetic.slnx"));

        Assert.Equal(4242, exception.ProcessId);
        Assert.Equal(1, factory.Process.KillCount);
        Assert.Equal(2, factory.Process.WaitCount);
        Assert.False(factory.Process.HasExited);
    }

    [Fact]
    public void UnconfirmedTerminationPreservesOwnedStagingAcrossRecovery()
    {
        using var temporary = new PackagerTemporaryDirectory();
        string repository = FakeRepository.Create(Path.Combine(temporary.RootPath, "repository"));
        string output = Path.Combine(temporary.RootPath, "output");
        var runner = new DotNetProcessRunner(
            new NeverExitsProcessFactory(),
            executionTimeout: TimeSpan.FromMilliseconds(20),
            terminationTimeout: TimeSpan.FromMilliseconds(20));
        var builder = new EnginePayloadBuilder(runner, temporary.RootPath);

        Assert.Throws<DotNetProcessTerminationException>(
            () => builder.Build(repository, output, "0.6.0-dev", "0.6.0-sdk"));
        string staging = Assert.Single(Directory.EnumerateDirectories(Path.Combine(output, ".staging")));

        new Karpik.Engine.Tooling.AtomicDirectoryPublisher(output).Recover();

        Assert.True(Directory.Exists(staging));
        Assert.True(File.Exists(Path.Combine(staging, ".process-termination-unconfirmed")));
    }

    private sealed class NeverExitsProcessFactory : IDotNetChildProcessFactory
    {
        public NeverExitsProcess Process { get; } = new();

        public IDotNetChildProcess Create(ProcessStartInfo startInfo) => Process;
    }

    private sealed class NeverExitsProcess : IDotNetChildProcess
    {
        public int Id => 4242;
        public bool HasExited => false;
        public int ExitCode => throw new InvalidOperationException();
        public int KillCount { get; private set; }
        public int WaitCount { get; private set; }

        public void Start()
        {
        }

        public Task<string> ReadStandardOutputToEndAsync() => Task.FromResult(string.Empty);

        public Task<string> ReadStandardErrorToEndAsync() => Task.FromResult(string.Empty);

        public async Task WaitForExitAsync(CancellationToken cancellationToken)
        {
            WaitCount++;
            await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
        }

        public void Kill(bool entireProcessTree) => KillCount++;

        public void Dispose()
        {
        }
    }
}
