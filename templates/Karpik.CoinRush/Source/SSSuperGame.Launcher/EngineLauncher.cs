using System.Diagnostics;
using System.Runtime.InteropServices;

namespace SSSuperGame.Launcher;

/// <summary>Starts a game runtime bundle with the installed KarpikEngine runner.</summary>
internal static class EngineLauncher
{
    /// <summary>Locates the runner and runtime bundle, then waits for the runner to exit.</summary>
    /// <param name="side">The engine side to launch.</param>
    /// <param name="runtimeProjectName">The project that produces the runtime bundle.</param>
    /// <param name="forwardedArguments">Command-line arguments passed to the runner.</param>
    /// <returns>The runner exit code, or 1 when startup prerequisites are missing.</returns>
    public static async Task<int> RunAsync(
        string side,
        string runtimeProjectName,
        string[] forwardedArguments)
    {
        string? engineRoot = Environment.GetEnvironmentVariable("KarpikEngineRoot");
        if (string.IsNullOrWhiteSpace(engineRoot) ||
            !Path.IsPathFullyQualified(engineRoot) ||
            !Directory.Exists(engineRoot))
        {
            Console.Error.WriteLine(
                "KarpikEngineRoot must point to an installed KarpikEngine directory.");
            return 1;
        }

        string runnerName = OperatingSystem.IsWindows()
            ? "Karpik.Engine.Core.Runner.exe"
            : "Karpik.Engine.Core.Runner";
        string runnerPath = Path.Combine(
            engineRoot,
            "runners",
            side.ToLowerInvariant(),
            runnerName);
        if (!File.Exists(runnerPath))
        {
            Console.Error.WriteLine($"KarpikEngine runner was not found: {runnerPath}");
            return 1;
        }

        DirectoryInfo outputDirectory = new(AppContext.BaseDirectory);
        DirectoryInfo? sourceDirectory =
            outputDirectory.Parent?.Parent?.Parent?.Parent;
        if (sourceDirectory is null)
        {
            Console.Error.WriteLine(
                $"Could not resolve the game Source directory from {AppContext.BaseDirectory}");
            return 1;
        }

        string configuration = outputDirectory.Parent!.Name;
        string targetFramework = outputDirectory.Name;
        string bundlePath = Path.Combine(
            sourceDirectory.FullName,
            runtimeProjectName,
            "bin",
            configuration,
            targetFramework,
            "karpik-bundle");
        if (!Directory.Exists(bundlePath))
        {
            Console.Error.WriteLine(
                $"Runtime bundle was not found: {bundlePath}{Environment.NewLine}" +
                $"Build the {runtimeProjectName} project before launching.");
            return 1;
        }

        var startInfo = new ProcessStartInfo
        {
            FileName = runnerPath,
            UseShellExecute = false,
            WorkingDirectory = bundlePath
        };
        AddArgument("--side", side);
        AddArgument("--bundle", bundlePath);
        AddArgument("--engine-root", engineRoot);
        foreach (string argument in forwardedArguments)
        {
            startInfo.ArgumentList.Add(argument);
        }
        AddNativeSearchPath(startInfo, engineRoot);

        using Process? process = Process.Start(startInfo);
        if (process is null)
        {
            Console.Error.WriteLine($"Failed to start KarpikEngine runner: {runnerPath}");
            return 1;
        }

        await process.WaitForExitAsync();
        return process.ExitCode;

        void AddArgument(string name, string value)
        {
            startInfo.ArgumentList.Add(name);
            startInfo.ArgumentList.Add(value);
        }
    }

    /// <summary>Adds installed native libraries to the child process search path.</summary>
    /// <param name="startInfo">The child process configuration to update.</param>
    /// <param name="engineRoot">The installed engine directory.</param>
    private static void AddNativeSearchPath(
        ProcessStartInfo startInfo,
        string engineRoot)
    {
        string nativeDirectory = Path.Combine(engineRoot, "native");
        if (!Directory.Exists(nativeDirectory))
        {
            return;
        }

        string path = startInfo.Environment["PATH"]
                      ?? Environment.GetEnvironmentVariable("PATH")
                      ?? string.Empty;
        string runtimeIdentifier = RuntimeInformation.RuntimeIdentifier;
        string architecture = runtimeIdentifier[
            (runtimeIdentifier.LastIndexOf('-') + 1)..];
        string? platform = OperatingSystem.IsWindows() ? "win"
            : OperatingSystem.IsLinux() ? "linux"
            : OperatingSystem.IsMacOS() ? "osx"
            : null;
        if (platform is not null)
        {
            string platformNativeDirectory = Path.Combine(
                nativeDirectory,
                $"{platform}-{architecture}");
            if (Directory.Exists(platformNativeDirectory))
            {
                path = platformNativeDirectory + Path.PathSeparator + path;
            }
        }

        startInfo.Environment["PATH"] =
            nativeDirectory + Path.PathSeparator + path;
    }
}
