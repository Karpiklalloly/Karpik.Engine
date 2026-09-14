using System.Diagnostics;
using System.Text;
using System.Text.RegularExpressions;
using Xunit;

namespace Karpik.Engine.Packager.Tests;

public sealed class UpdateKarpikSdkScriptTests
{
    [Fact]
    public void ScriptUsesEditableSdkVersionAndPublishesAnAtomicPayload()
    {
        string repositoryRoot = Path.GetFullPath(
            Path.Combine(AppContext.BaseDirectory, "..", "..", "..", ".."));
        string scriptPath = Path.Combine(repositoryRoot, "_scripts", "Update-KarpikSdk.ps1");

        Assert.True(File.Exists(scriptPath), $"Missing SDK update script: {scriptPath}");
        string script = File.ReadAllText(scriptPath);

        Assert.Matches(
            new Regex(@"(?m)^\$SdkVersionBase\s*=\s*""[^""]+""\s*$", RegexOptions.CultureInvariant),
            script);
        Assert.Matches(
            new Regex(@"\$SdkVersion\s*=\s*""\$SdkVersionBase-\$\(Get-Date -Format 'yyyyMMdd-HHmmss'\)""", RegexOptions.CultureInvariant),
            script);
        Assert.Contains("Karpik.Engine.Packager", script, StringComparison.Ordinal);
        Assert.Contains("--sdk-version", script, StringComparison.Ordinal);
        Assert.Contains("ArchivedEngines", script, StringComparison.Ordinal);
        Assert.Contains("dotnet nuget", script, StringComparison.Ordinal);
        Assert.DoesNotContain("Copy-Item", script, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("TrimEndingDirectorySeparator", script, StringComparison.Ordinal);
    }

    [Fact]
    public void EnvironmentUpdaterSelectsInstallationForCurrentAndFutureProcesses()
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        string repositoryRoot = Path.GetFullPath(
            Path.Combine(AppContext.BaseDirectory, "..", "..", "..", ".."));
        string modulePath = Path.Combine(repositoryRoot, "Karpik.Sdk.Environment.psm1");
        Assert.True(File.Exists(modulePath), $"Missing SDK environment module: {modulePath}");

        string expectedRoot = Path.Combine(
            Path.GetTempPath(),
            "karpik-sdk-environment-test",
            Guid.NewGuid().ToString("N"));
        string? previousUserValue = Environment.GetEnvironmentVariable(
            "KarpikEngineRoot",
            EnvironmentVariableTarget.User);

        try
        {
            string command = $"""
                Import-Module '{EscapePowerShellLiteral(modulePath)}' -Force
                Set-KarpikEngineRootEnvironment -InstallationRoot '{EscapePowerShellLiteral(expectedRoot)}'
                [Console]::WriteLine($env:KarpikEngineRoot)
                [Console]::WriteLine([Environment]::GetEnvironmentVariable('KarpikEngineRoot', 'User'))
                """;
            string encodedCommand = Convert.ToBase64String(Encoding.Unicode.GetBytes(command));

            ProcessStartInfo startInfo = new("powershell.exe")
            {
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true
            };
            startInfo.ArgumentList.Add("-NoLogo");
            startInfo.ArgumentList.Add("-NoProfile");
            startInfo.ArgumentList.Add("-NonInteractive");
            startInfo.ArgumentList.Add("-EncodedCommand");
            startInfo.ArgumentList.Add(encodedCommand);

            using Process process = Process.Start(startInfo)
                ?? throw new InvalidOperationException("Failed to start PowerShell.");
            string standardOutput = process.StandardOutput.ReadToEnd();
            string standardError = process.StandardError.ReadToEnd();
            process.WaitForExit();

            Assert.True(
                process.ExitCode == 0,
                $"PowerShell exited with code {process.ExitCode}.{Environment.NewLine}{standardError}");
            string[] outputLines = standardOutput
                .Split(Environment.NewLine, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            Assert.Equal([expectedRoot, expectedRoot], outputLines);
        }
        finally
        {
            Environment.SetEnvironmentVariable(
                "KarpikEngineRoot",
                previousUserValue,
                EnvironmentVariableTarget.User);
        }
    }

    private static string EscapePowerShellLiteral(string value)
    {
        return value.Replace("'", "''", StringComparison.Ordinal);
    }
}
