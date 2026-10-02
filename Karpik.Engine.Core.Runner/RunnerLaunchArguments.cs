namespace Karpik.Engine.Core.Runner;

using Karpik.Engine.Tooling;

public sealed record RunnerLaunchArguments(
    Side Side,
    string BundlePath,
    string EngineRoot,
    string? PipeName,
    string? State,
    string? StateFile,
    bool WaitForDebugger)
{
    public static RunnerLaunchArguments Parse(string[] args)
        => Parse(args, AppContext.BaseDirectory);

    internal static RunnerLaunchArguments Parse(string[] args, string runnerBaseDirectory)
    {
        ArgumentNullException.ThrowIfNull(args);
        var values = new Dictionary<string, string>(StringComparer.Ordinal);
        bool waitForDebugger = false;
        for (int index = 0; index < args.Length; index++)
        {
            string argument = args[index];
            if (argument == "--wait-for-debugger")
            {
                if (waitForDebugger)
                {
                    throw new ArgumentException("Duplicate --wait-for-debugger argument.", nameof(args));
                }
                waitForDebugger = true;
                continue;
            }

            int equals = argument.IndexOf('=');
            string name;
            string value;
            if (equals > 0)
            {
                name = argument[..equals];
                value = argument[(equals + 1)..];
            }
            else
            {
                name = argument;
                if (index + 1 >= args.Length || args[index + 1].StartsWith("--", StringComparison.Ordinal))
                {
                    throw new ArgumentException($"Argument '{name}' requires a value.", nameof(args));
                }
                value = args[++index];
            }
            if (name is not ("--bundle" or "--engine-root" or "--side" or "--pipe-name" or "--state" or "--state-file"))
            {
                throw new ArgumentException($"Unknown runner argument: {name}", nameof(args));
            }
            if (string.IsNullOrWhiteSpace(value) || !values.TryAdd(name, value))
            {
                throw new ArgumentException($"Runner argument '{name}' is empty or duplicated.", nameof(args));
            }
        }

        if (!values.TryGetValue("--side", out string? sideValue)
            || !Enum.TryParse(sideValue, ignoreCase: false, out Side side)
            || side is not (Side.Client or Side.Server))
        {
            throw new ArgumentException("Runner requires exactly one --side Client|Server argument.", nameof(args));
        }
        if (!values.TryGetValue("--bundle", out string? bundle))
        {
            throw new ArgumentException("Runner requires exactly one --bundle <absolute-path> argument.", nameof(args));
        }
        string validatedBundle = RuntimeBundleLayout.Validate(bundle, side);
        if (!values.TryGetValue("--engine-root", out string? engineRoot))
        {
            throw new ArgumentException("Runner requires exactly one --engine-root <absolute-path> argument.", nameof(args));
        }
        string validatedEngineRoot = ValidateEngineRoot(engineRoot);
        ValidateRunnerOwnership(validatedEngineRoot, side, runnerBaseDirectory);
        values.TryGetValue("--pipe-name", out string? pipeName);
        values.TryGetValue("--state", out string? state);
        values.TryGetValue("--state-file", out string? stateFile);
        if (state is not null && stateFile is not null)
        {
            throw new ArgumentException("Runner accepts either --state or --state-file, never both.", nameof(args));
        }
        if (stateFile is not null)
        {
            stateFile = ValidateStateFile(validatedBundle, stateFile);
        }
        return new RunnerLaunchArguments(side, validatedBundle, validatedEngineRoot, pipeName, state, stateFile, waitForDebugger);
    }

    private static void ValidateRunnerOwnership(string engineRoot, Side side, string runnerBaseDirectory)
    {
        if (!Path.IsPathFullyQualified(runnerBaseDirectory))
        {
            throw new InvalidDataException("Runner base directory must be absolute.");
        }
        string actual = Path.GetFullPath(runnerBaseDirectory)
            .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        string expected = Path.GetFullPath(Path.Combine(engineRoot, "runners", side.ToString().ToLowerInvariant()))
            .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        StringComparison comparison = OperatingSystem.IsWindows()
            ? StringComparison.OrdinalIgnoreCase
            : StringComparison.Ordinal;
        if (!string.Equals(actual, expected, comparison))
        {
            throw new InvalidDataException(
                $"The running {side} Runner does not belong to the selected engine installation '{engineRoot}'.");
        }
    }

    private static string ValidateEngineRoot(string engineRoot)
    {
        if (!Path.IsPathFullyQualified(engineRoot))
        {
            throw new ArgumentException("Runner engine root path must be absolute.", nameof(engineRoot));
        }
        string root = Path.GetFullPath(engineRoot).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        if (!Directory.Exists(root))
        {
            throw new DirectoryNotFoundException($"Engine installation does not exist: {root}");
        }
        RuntimeBundleLayout.EnsureExistingPathHasNoReparsePoints(root);
        EngineInstallationValidationResult validation = new EngineInstallationValidator().Validate(root);
        if (!validation.IsValid)
        {
            throw new InvalidDataException($"Runner requires a valid installed engine payload: {validation.Message}");
        }
        return root;
    }

    private static string ValidateStateFile(string bundleRoot, string stateFile)
    {
        if (!Path.IsPathFullyQualified(stateFile))
        {
            throw new ArgumentException("Runner state file path must be absolute.", nameof(stateFile));
        }
        string fullPath = Path.GetFullPath(stateFile);
        string stateRoot = Path.GetFullPath(Path.Combine(bundleRoot, "reload", "state"))
            .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
            + Path.DirectorySeparatorChar;
        StringComparison comparison = OperatingSystem.IsWindows()
            ? StringComparison.OrdinalIgnoreCase
            : StringComparison.Ordinal;
        if (!fullPath.StartsWith(stateRoot, comparison))
        {
            throw new InvalidDataException("Runner state file must stay within the runtime bundle reload/state directory.");
        }
        if (!File.Exists(fullPath))
        {
            throw new FileNotFoundException("Runner state file does not exist.", fullPath);
        }
        RuntimeBundleLayout.EnsureExistingPathHasNoReparsePoints(fullPath);
        return fullPath;
    }
}
