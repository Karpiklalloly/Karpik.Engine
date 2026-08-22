using Karpik.Engine.Core;

namespace Karpik.Engine.Core.Runner;

/// <summary>
/// Game-specific Static host entry point. The launcher executable that owns the
/// generated <c>GeneratedRuntimeComposition</c> calls <see cref="RunAsync"/> with
/// a direct instance — no dynamic module loader, no plugin load context, no
/// reflection. See the Dynamic-only boundary contract test in Runner tests.
/// </summary>
public static class StaticEngineHost
{
    public static Task<int> RunAsync(
        Side side,
        IStaticRuntimeComposition composition,
        string[] args,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(composition);

        Console.WriteLine("[Worker] Starting...");
        try
        {
            RunnerLaunchArguments launch = StaticLaunchArguments.Parse(args, side);
            new WorkerHost().Run(
                launch,
                (hostSide, _, _, bootstrap) =>
                {
                    var runner = (EngineRunner)bootstrap.Runner;
                    runner.RegisterStaticComposition(composition);
                    Console.WriteLine($"[StaticHost] Registered static composition for side {hostSide}");
                    return new WorkerRuntimeConfiguration(ResolveModuleDirectory(launch.BundlePath), null);
                },
                cancellationToken);
            return Task.FromResult(0);
        }
        catch (OperationCanceledException)
        {
            return Task.FromResult(0);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[Worker] Engine crashed: {ex}");
            return Task.FromResult(1);
        }
    }

    // A static runtime output carries no managed module staging directory, so the
    // worker reports its own base directory; game modules are compiled into the host.
    private static string ResolveModuleDirectory(string bundlePath) => AppContext.BaseDirectory;
}

file static class StaticLaunchArguments
{
    public static RunnerLaunchArguments Parse(string[] args, Side hostSide)
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
                throw new ArgumentException($"Unknown static host argument: {name}", nameof(args));
            }
            if (string.IsNullOrWhiteSpace(value) || !values.TryAdd(name, value))
            {
                throw new ArgumentException($"Static host argument '{name}' is empty or duplicated.", nameof(args));
            }
        }

        if (values.TryGetValue("--side", out string? sideValue)
            && (!Enum.TryParse(sideValue, ignoreCase: false, out Side side)
                || side is not (Side.Client or Side.Server)
                || side != hostSide))
        {
            throw new ArgumentException(
                $"Static host was built for {hostSide} but received --side {sideValue}.", nameof(args));
        }

        values.TryGetValue("--bundle", out string? bundle);
        string validatedBundle = bundle is null
            ? string.Empty
            : RuntimeBundleLayout.ValidateStatic(bundle, hostSide);

        values.TryGetValue("--engine-root", out string? engineRoot);
        string validatedEngineRoot = ValidateOptionalEngineRoot(engineRoot);

        values.TryGetValue("--pipe-name", out string? pipeName);
        values.TryGetValue("--state", out string? state);
        values.TryGetValue("--state-file", out string? stateFile);
        if (state is not null && stateFile is not null)
        {
            throw new ArgumentException("Static host accepts either --state or --state-file, never both.", nameof(args));
        }
        if (stateFile is not null && !Path.IsPathFullyQualified(stateFile))
        {
            throw new ArgumentException("Static host state file path must be absolute.", nameof(args));
        }

        return new RunnerLaunchArguments(hostSide, validatedBundle, validatedEngineRoot, pipeName, state, stateFile, waitForDebugger);
    }

    private static string ValidateOptionalEngineRoot(string? engineRoot)
    {
        if (string.IsNullOrEmpty(engineRoot))
        {
            return string.Empty;
        }
        if (!Path.IsPathFullyQualified(engineRoot))
        {
            throw new ArgumentException("Static host engine root path must be absolute.", nameof(engineRoot));
        }
        string root = Path.GetFullPath(engineRoot).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        if (!Directory.Exists(root))
        {
            throw new DirectoryNotFoundException($"Static host engine installation does not exist: {root}");
        }
        return root;
    }
}
