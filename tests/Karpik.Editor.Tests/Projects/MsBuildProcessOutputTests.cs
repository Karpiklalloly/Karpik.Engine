using System.Diagnostics;
using System.Text;
using Karpik.Editor;
using Xunit;

namespace Karpik.Editor.Tests.Projects;

public sealed class MsBuildProcessOutputTests
{
    [Fact]
    public async Task Start_RedirectedText_UsesExplicitUtf8Encoding()
    {
        Assert.SkipWhen(!OperatingSystem.IsWindows(), "The no-op child process uses cmd.exe.");
        var startInfo = new ProcessStartInfo
        {
            FileName = "cmd.exe",
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true
        };
        startInfo.ArgumentList.Add("/d");
        startInfo.ArgumentList.Add("/c");
        startInfo.ArgumentList.Add("exit");
        startInfo.ArgumentList.Add("0");
        IMsBuildProcess process = new SystemMsBuildProcessFactory().Start(startInfo);

        try
        {
            Assert.Equal(Encoding.UTF8.WebName, startInfo.StandardOutputEncoding?.WebName);
            Assert.Equal(Encoding.UTF8.WebName, startInfo.StandardErrorEncoding?.WebName);
            await process.WaitForExitAsync(TestContext.Current.CancellationToken);
        }
        finally
        {
            await process.DisposeAsync();
        }
    }

    [Fact]
    public async Task ReadStandardOutputAsync_Utf8Bytes_PreservesCyrillicText()
    {
        Assert.SkipWhen(!OperatingSystem.IsWindows(), "The deterministic raw-byte producer uses cmd.exe.");
        const string expected = "Определение проектов для восстановления...";
        string path = Path.Combine(Path.GetTempPath(), $"karpik-utf8-output-{Guid.NewGuid():N}.txt");
        await File.WriteAllTextAsync(
            path,
            expected,
            new UTF8Encoding(encoderShouldEmitUTF8Identifier: false),
            TestContext.Current.CancellationToken);

        var startInfo = new ProcessStartInfo
        {
            FileName = "cmd.exe",
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true
        };
        startInfo.ArgumentList.Add("/d");
        startInfo.ArgumentList.Add("/c");
        startInfo.ArgumentList.Add("type");
        startInfo.ArgumentList.Add(path);
        IMsBuildProcess process = new SystemMsBuildProcessFactory().Start(startInfo);

        try
        {
            Task<string> read = process.ReadStandardOutputAsync(
                maximumCharacters: 1024,
                TestContext.Current.CancellationToken);
            await process.WaitForExitAsync(TestContext.Current.CancellationToken);

            Assert.Equal(expected, await read);
            Assert.Equal(0, process.ExitCode);
        }
        finally
        {
            await process.DisposeAsync();
            File.Delete(path);
        }
    }

    [Fact]
    public async Task ReadBoundedAsync_WhenLimitIsExceeded_DrainsStreamBeforeFailing()
    {
        Assert.SkipWhen(!OperatingSystem.IsWindows(), "The deterministic high-volume producer uses PowerShell.");
        var startInfo = new ProcessStartInfo
        {
            FileName = "powershell.exe",
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true
        };
        startInfo.ArgumentList.Add("-NoProfile");
        startInfo.ArgumentList.Add("-Command");
        startInfo.ArgumentList.Add("[Console]::Out.Write('x' * 1048576)");
        IMsBuildProcess process = new SystemMsBuildProcessFactory().Start(startInfo);
        Task<string> read = process.ReadStandardOutputAsync(
            maximumCharacters: 128,
            TestContext.Current.CancellationToken);

        try
        {
            await process.WaitForExitAsync(TestContext.Current.CancellationToken)
                .WaitAsync(TimeSpan.FromSeconds(15), TestContext.Current.CancellationToken);
            await Assert.ThrowsAsync<InvalidDataException>(() => read);
            Assert.Equal(0, process.ExitCode);
        }
        finally
        {
            try
            {
                process.Kill(entireProcessTree: true);
            }
            catch (InvalidOperationException)
            {
            }
            using var exitTimeout = new CancellationTokenSource(TimeSpan.FromSeconds(15));
            await process.WaitForExitAsync(exitTimeout.Token);
            await process.DisposeAsync();
        }
    }
}
