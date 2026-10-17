using Karpik.Content.Core;

namespace Karpik.Content.Tool;

internal static class Program
{
    private const int ExitSuccess = 0;
    private const int ExitUsage = 1;
    private const int ExitValidation = 2;
    private const int ExitUnexpected = 3;

    public static int Main(string[] args)
    {
        if (args.Length == 0)
        {
            PrintUsage();
            return ExitUsage;
        }

        string command = args[0].ToLowerInvariant();
        try
        {
            return command switch
            {
                "build" => RunBuild(args.Skip(1).ToArray()),
                "validate" => RunValidate(args.Skip(1).ToArray()),
                "create" => RunCreate(args.Skip(1).ToArray()),
                "list" => RunList(args.Skip(1).ToArray()),
                "why" => RunWhy(args.Skip(1).ToArray()),
                "--help" or "-h" or "help" => RunHelp(),
                _ => UnknownCommand(command)
            };
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"unexpected error: {ex.Message}");
            return ExitUnexpected;
        }
    }

    private static int RunHelp()
    {
        PrintUsage();
        return ExitSuccess;
    }

    private static int UnknownCommand(string command)
    {
        Console.Error.WriteLine($"unknown command '{command}'");
        PrintUsage();
        return ExitUsage;
    }

    private static void PrintUsage()
    {
        Console.WriteLine("Karpik Content Tool");
        Console.WriteLine("Usage:");
        Console.WriteLine("  content build    --source <dir> --output <dir> --namespace <id> --target <Client|Server>");
        Console.WriteLine("  content validate --source <dir> --namespace <id> --target <Client|Server>");
        Console.WriteLine("  content create   --source <dir> --file <relative-source-path> --namespace <id>");
        Console.WriteLine("  content list     --manifest <path>");
        Console.WriteLine("  content why      --manifest <path> <asset-guid>");
    }

    private static int RunBuild(string[] args)
    {
        string? source = null;
        string? output = null;
        string? ns = null;
        string? target = null;

        for (int i = 0; i < args.Length; i++)
        {
            string arg = args[i];
            if (arg == "--source" && i + 1 < args.Length) source = args[++i];
            else if (arg == "--output" && i + 1 < args.Length) output = args[++i];
            else if (arg == "--namespace" && i + 1 < args.Length) ns = args[++i];
            else if (arg == "--target" && i + 1 < args.Length) target = args[++i];
            else if (arg == "--help" || arg == "-h") { PrintBuildUsage(); return ExitSuccess; }
            else { Console.Error.WriteLine($"unknown argument '{arg}' for build"); PrintBuildUsage(); return ExitUsage; }
        }

        if (string.IsNullOrWhiteSpace(source) || string.IsNullOrWhiteSpace(output) || string.IsNullOrWhiteSpace(ns) || string.IsNullOrWhiteSpace(target))
        {
            Console.Error.WriteLine("build requires --source, --output, --namespace, --target");
            PrintBuildUsage();
            return ExitUsage;
        }

        if (!AssetTargets.TryParse(target, out AssetTarget buildTarget))
        {
            Console.Error.WriteLine($"invalid --target '{target}'. Expected 'Client' or 'Server'.");
            PrintBuildUsage();
            return ExitUsage;
        }

        var coordinator = new ContentBuildCoordinator();
        var options = new ContentBuildOptions { SourceRoot = source, OutputRoot = output, Namespace = ns, Target = buildTarget };
        ContentBuildResult result = coordinator.Build(options);

        PrintDiagnostics(result.Diagnostics);

        if (!result.Success)
        {
            return ExitValidation;
        }

        Console.WriteLine($"build succeeded: {result.Manifest?.Entries.Count ?? 0} assets");
        if (result.Manifest is not null)
        {
            // Optionally print manifest path?
            string manifestPath = Path.Combine(Path.GetFullPath(output), "manifest.json");
            Console.WriteLine($"manifest: {manifestPath}");
        }

        return ExitSuccess;
    }

    private static void PrintBuildUsage()
    {
        Console.WriteLine("Usage: content build --source <dir> --output <dir> --namespace <id> --target <Client|Server>");
    }

    private static int RunValidate(string[] args)
    {
        string? source = null;
        string? ns = null;
        string? target = null;

        for (int i = 0; i < args.Length; i++)
        {
            string arg = args[i];
            if (arg == "--source" && i + 1 < args.Length) source = args[++i];
            else if (arg == "--namespace" && i + 1 < args.Length) ns = args[++i];
            else if (arg == "--target" && i + 1 < args.Length) target = args[++i];
            else if (arg == "--help" || arg == "-h") { PrintValidateUsage(); return ExitSuccess; }
            else { Console.Error.WriteLine($"unknown argument '{arg}' for validate"); PrintValidateUsage(); return ExitUsage; }
        }

        if (string.IsNullOrWhiteSpace(source) || string.IsNullOrWhiteSpace(ns) || string.IsNullOrWhiteSpace(target))
        {
            Console.Error.WriteLine("validate requires --source, --namespace, --target");
            PrintValidateUsage();
            return ExitUsage;
        }

        if (!AssetTargets.TryParse(target, out AssetTarget validateTarget))
        {
            Console.Error.WriteLine($"invalid --target '{target}'. Expected 'Client' or 'Server'.");
            PrintValidateUsage();
            return ExitUsage;
        }

        var coordinator = new ContentBuildCoordinator();
        var options = new ContentBuildOptions { SourceRoot = source, OutputRoot = Path.GetTempPath(), Namespace = ns, Target = validateTarget };
        ContentBuildResult result = coordinator.Validate(options);

        PrintDiagnostics(result.Diagnostics);

        if (!result.Success)
        {
            return ExitValidation;
        }

        Console.WriteLine("validate succeeded");
        return ExitSuccess;
    }

    private static void PrintValidateUsage()
    {
        Console.WriteLine("Usage: content validate --source <dir> --namespace <id> --target <Client|Server>");
    }

    private static int RunCreate(string[] args)
    {
        string? source = null;
        string? file = null;
        string? ns = null;

        for (int i = 0; i < args.Length; i++)
        {
            string arg = args[i];
            if (arg == "--source" && i + 1 < args.Length) source = args[++i];
            else if (arg == "--file" && i + 1 < args.Length) file = args[++i];
            else if (arg == "--namespace" && i + 1 < args.Length) ns = args[++i];
            else if (arg == "--help" || arg == "-h") { PrintCreateUsage(); return ExitSuccess; }
            else { Console.Error.WriteLine($"unknown argument '{arg}' for create"); PrintCreateUsage(); return ExitUsage; }
        }

        if (string.IsNullOrWhiteSpace(source) || string.IsNullOrWhiteSpace(file) || string.IsNullOrWhiteSpace(ns))
        {
            Console.Error.WriteLine("create requires --source, --file, and --namespace");
            PrintCreateUsage();
            return ExitUsage;
        }

        string sourceRoot = Path.GetFullPath(source);
        if (!Directory.Exists(sourceRoot))
        {
            Console.Error.WriteLine($"source directory not found: {sourceRoot}");
            return ExitValidation;
        }
        if (!IsValidNamespace(ns))
        {
            Console.Error.WriteLine($"invalid namespace '{ns}'");
            return ExitValidation;
        }

        string sourceFile = Path.GetFullPath(Path.IsPathRooted(file)
            ? file
            : Path.Combine(sourceRoot, file));
        if (!IsContained(sourceRoot, sourceFile))
        {
            Console.Error.WriteLine($"source file escapes source directory: {sourceFile}");
            return ExitValidation;
        }
        if (!File.Exists(sourceFile))
        {
            Console.Error.WriteLine($"source file not found: {sourceFile}");
            return ExitValidation;
        }
        string relativePath = Path.GetRelativePath(sourceRoot, sourceFile);
        if (!ContentMetaTemplate.TryCreate(relativePath, ns, out string metaJson))
        {
            Console.Error.WriteLine($"create supports only .json, .png, .jpg, and .jpeg source files: {sourceFile}");
            return ExitValidation;
        }

        string metaPath = sourceFile + ".meta";
        try
        {
            using FileStream stream = new(metaPath, FileMode.CreateNew, FileAccess.Write, FileShare.None);
            using var writer = new StreamWriter(stream);
            writer.Write(metaJson);
        }
        catch (IOException) when (File.Exists(metaPath))
        {
            Console.Error.WriteLine($"meta file already exists: {metaPath}");
            return ExitValidation;
        }

        AssetMeta meta = AssetMeta.Parse(metaJson, relativePath + ".meta", []);
        Console.WriteLine($"created meta: {metaPath}");
        Console.WriteLine($"assetId: {meta.AssetId.ToCanonicalString()}");
        Console.WriteLine($"logicalName: {meta.LogicalName}");
        return ExitSuccess;
    }

    private static void PrintCreateUsage()
    {
        Console.WriteLine("Usage: content create --source <dir> --file <relative-source-path> --namespace <id>");
    }

    private static int RunList(string[] args)
    {
        string? manifestPath = null;

        for (int i = 0; i < args.Length; i++)
        {
            string arg = args[i];
            if (arg == "--manifest" && i + 1 < args.Length) manifestPath = args[++i];
            else if (arg == "--help" || arg == "-h") { PrintListUsage(); return ExitSuccess; }
            else if (!arg.StartsWith("--") && manifestPath is null) manifestPath = arg; // allow positional
            else { Console.Error.WriteLine($"unknown argument '{arg}' for list"); PrintListUsage(); return ExitUsage; }
        }

        if (string.IsNullOrWhiteSpace(manifestPath))
        {
            Console.Error.WriteLine("list requires --manifest <path>");
            PrintListUsage();
            return ExitUsage;
        }

        if (!File.Exists(manifestPath))
        {
            Console.Error.WriteLine($"manifest not found: {manifestPath}");
            return ExitValidation;
        }

        ContentManifest manifest;
        try
        {
            manifest = ContentManifest.LoadFromFile(manifestPath);
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"failed to load manifest: {ex.Message}");
            return ExitValidation;
        }

        // Sorted output by AssetId
        foreach (ContentManifestEntry entry in manifest.Entries.OrderBy(e => e.AssetId.Value))
        {
            // Stable deterministic line: assetId logicalName declaredType locator size
            Console.WriteLine($"{entry.AssetId.ToCanonicalString()} {entry.LogicalName} {entry.DeclaredType} {entry.ArtifactLocator} {entry.Size}");
        }

        return ExitSuccess;
    }

    private static void PrintListUsage()
    {
        Console.WriteLine("Usage: content list --manifest <path>");
    }

    private static int RunWhy(string[] args)
    {
        string? manifestPath = null;
        string? assetIdStr = null;

        for (int i = 0; i < args.Length; i++)
        {
            string arg = args[i];
            if (arg == "--manifest" && i + 1 < args.Length) manifestPath = args[++i];
            else if (arg == "--help" || arg == "-h") { PrintWhyUsage(); return ExitSuccess; }
            else if (!arg.StartsWith("--") && manifestPath is null) manifestPath = arg;
            else if (!arg.StartsWith("--") && assetIdStr is null) assetIdStr = arg;
            else { Console.Error.WriteLine($"unknown argument '{arg}' for why"); PrintWhyUsage(); return ExitUsage; }
        }

        // Also support: why --manifest <path> <guid> where guid is positional after manifest flag
        // Our loop above handles --manifest <path> then next positional is assetId
        // Alternative: if args length is 2 and no flags, first is manifest, second is guid
        if (manifestPath is not null && assetIdStr is null && args.Length >= 2)
        {
            // Check if last arg is guid and not manifest
            string last = args[^1];
            if (!string.Equals(last, manifestPath, StringComparison.Ordinal) && AssetId.TryParse(last, out _))
            {
                assetIdStr = last;
            }
        }

        if (string.IsNullOrWhiteSpace(manifestPath) || string.IsNullOrWhiteSpace(assetIdStr))
        {
            Console.Error.WriteLine("why requires --manifest <path> <asset-guid>");
            PrintWhyUsage();
            return ExitUsage;
        }

        if (!File.Exists(manifestPath))
        {
            Console.Error.WriteLine($"manifest not found: {manifestPath}");
            return ExitValidation;
        }

        if (!AssetId.TryParse(assetIdStr, out AssetId assetId))
        {
            Console.Error.WriteLine($"invalid assetId '{assetIdStr}'");
            return ExitUsage;
        }

        ContentManifest manifest;
        try
        {
            manifest = ContentManifest.LoadFromFile(manifestPath);
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"failed to load manifest: {ex.Message}");
            return ExitValidation;
        }

        var entryMap = manifest.Entries.ToDictionary(e => e.AssetId);
        if (!entryMap.TryGetValue(assetId, out ContentManifestEntry? rootEntry))
        {
            Console.Error.WriteLine($"asset '{assetId.ToCanonicalString()}' not found in manifest");
            return ExitValidation;
        }

        // BFS/DFS to show dependency paths
        // Print root, then each direct dependency, and transitive with indentation or path
        Console.WriteLine($"{rootEntry.AssetId.ToCanonicalString()} {rootEntry.LogicalName} ({rootEntry.DeclaredType})");

        var visited = new HashSet<AssetId>();
        visited.Add(rootEntry.AssetId);

        void PrintDeps(AssetId current, string prefix, int depth)
        {
            if (!entryMap.TryGetValue(current, out ContentManifestEntry? curEntry)) return;
            var deps = curEntry.Dependencies.OrderBy(d => d.Value).ToList();
            for (int i = 0; i < deps.Count; i++)
            {
                AssetId dep = deps[i];
                bool isLast = i == deps.Count - 1;
                string connector = isLast ? "└─ " : "├─ ";
                if (!entryMap.TryGetValue(dep, out ContentManifestEntry? depEntry))
                {
                    Console.WriteLine($"{prefix}{connector}{dep.ToCanonicalString()} (missing)");
                    continue;
                }

                Console.WriteLine($"{prefix}{connector}{dep.ToCanonicalString()} {depEntry.LogicalName} ({depEntry.DeclaredType})");

                if (visited.Add(dep))
                {
                    string childPrefix = prefix + (isLast ? "   " : "│  ");
                    PrintDeps(dep, childPrefix, depth + 1);
                }
                else
                {
                    Console.WriteLine($"{prefix}{(isLast ? "   " : "│  ")}(cycle to {dep.ToCanonicalString()} truncated)");
                }
            }
        }

        PrintDeps(rootEntry.AssetId, "", 0);

        return ExitSuccess;
    }

    private static void PrintWhyUsage()
    {
        Console.WriteLine("Usage: content why --manifest <path> <asset-guid>");
    }

    private static bool IsContained(string root, string candidate)
    {
        string fullRoot = Path.GetFullPath(root).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        string fullCandidate = Path.GetFullPath(candidate);
        StringComparison comparison = OperatingSystem.IsWindows()
            ? StringComparison.OrdinalIgnoreCase
            : StringComparison.Ordinal;
        return string.Equals(fullRoot, fullCandidate, comparison) ||
               fullCandidate.StartsWith(fullRoot + Path.DirectorySeparatorChar, comparison);
    }

    private static bool IsValidNamespace(string value)
    {
        if (string.IsNullOrWhiteSpace(value) || value.Contains('/') || value.Contains('\\') || value.Contains(".."))
        {
            return false;
        }

        return value.All(character =>
            character is >= 'a' and <= 'z' or >= 'A' and <= 'Z' or >= '0' and <= '9' or '_' or '-' or '.');
    }

    private static void PrintDiagnostics(IReadOnlyList<ContentDiagnostic> diagnostics)
    {
        foreach (ContentDiagnostic d in diagnostics.OrderBy(x => x.Code).ThenBy(x => x.RelativePath).ThenBy(x => x.Message))
        {
            string severity = d.Severity == ContentDiagnosticSeverity.Error ? "error" : "warning";
            string loc = d.RelativePath is null ? string.Empty : $"{d.RelativePath}: ";
            Console.WriteLine($"{d.Code} {severity} {loc}{d.Message}");
            Console.Error.WriteLine($"{d.Code} {severity} {loc}{d.Message}");
        }
    }
}
