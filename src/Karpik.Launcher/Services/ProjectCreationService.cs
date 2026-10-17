using System.Diagnostics;
using Karpik.Engine.Tooling;
using Karpik.Launcher.Models;

namespace Karpik.Launcher.Services;

public sealed record ProjectCreationResult(bool IsSuccess, string Message, string? SolutionPath = null);

public sealed class ProjectCreationService
{
    public async Task<ProjectCreationResult> CreateAsync(string installationRoot, string sdkVersion, ProjectTemplate template, string parentDirectory, string projectName, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(projectName) || projectName.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0 || projectName is "." or ".." || projectName != Path.GetFileName(projectName))
            return new(false, "Project name is invalid.");
        if (!Directory.Exists(parentDirectory)) return new(false, "Project directory does not exist.");
        if (template.PackageFile != Path.GetFileName(template.PackageFile)) return new(false, "Template package is invalid.");
        string package = Path.Combine(installationRoot, "sdk", template.PackageFile);
        if (!File.Exists(package)) return new(false, "Template package does not exist.");
        string target = Path.Combine(parentDirectory, projectName);
        if (Directory.Exists(target) || File.Exists(target)) return new(false, "Project directory already exists.");
        string staging = Path.Combine(parentDirectory, $".karpik-create-{Guid.NewGuid():N}");
        string hive = Path.Combine(Path.GetTempPath(), "karpik-launcher-template-hives", Guid.NewGuid().ToString("N"));
        try
        {
            await RunAsync(["new", "install", package, "--debug:custom-hive", hive], cancellationToken);
            await RunAsync(["new", template.ShortName, "--name", projectName, "--output", staging, "--karpik-sdk-version", sdkVersion, "--debug:custom-hive", hive, "--no-update-check"], cancellationToken);
            string[] solutions = Directory.EnumerateFiles(staging, "*.slnx", SearchOption.TopDirectoryOnly).ToArray();
            if (solutions.Length != 1 || new GlobalJsonSdkVersionReader().Read(Path.Combine(staging, "global.json")).SdkVersion != sdkVersion)
                return new(false, "Generated project does not match the selected SDK.");
            Directory.Move(staging, target);
            return new(true, "Project created.", Path.Combine(target, Path.GetFileName(solutions[0])));
        }
        catch (Exception exception) when (exception is IOException or InvalidOperationException or OperationCanceledException)
        {
            return new(false, exception.Message);
        }
        finally
        {
            DeleteIfExists(staging);
            DeleteIfExists(hive);
        }
    }

    private static async Task RunAsync(IReadOnlyList<string> arguments, CancellationToken cancellationToken)
    {
        var start = new ProcessStartInfo("dotnet") { UseShellExecute = false, RedirectStandardError = true };
        foreach (string argument in arguments) start.ArgumentList.Add(argument);
        using var process = Process.Start(start) ?? throw new InvalidOperationException("Cannot start dotnet.");
        await process.WaitForExitAsync(cancellationToken);
        if (process.ExitCode != 0) throw new InvalidOperationException((await process.StandardError.ReadToEndAsync()).Trim() is { Length: > 0 } error ? error : "dotnet new failed.");
    }

    private static void DeleteIfExists(string path)
    {
        try { if (Directory.Exists(path)) Directory.Delete(path, true); }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }
}
