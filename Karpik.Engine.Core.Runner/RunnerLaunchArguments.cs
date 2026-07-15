namespace Karpik.Engine.Core.Runner;

public sealed record RunnerLaunchArguments(
    Side Side,
    string BundlePath,
    string? PipeName,
    string? State,
    string? StateFile,
    bool WaitForDebugger)
{
    public static RunnerLaunchArguments Parse(string[] args)
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
            if (name is not ("--bundle" or "--side" or "--pipe-name" or "--state" or "--state-file"))
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
        return new RunnerLaunchArguments(side, validatedBundle, pipeName, state, stateFile, waitForDebugger);
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
        if (RuntimeBundleLayout.IsReparsePoint(fullPath))
        {
            throw new InvalidDataException($"Runner state file must not be a link or reparse point: {fullPath}");
        }
        return fullPath;
    }
}
