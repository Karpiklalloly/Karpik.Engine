using System.Diagnostics;
using System.Reflection;
using System.Text.Json;
using Karpik.Content.Core;

return await GenerationProcesses.RunAsync(args);

internal static class GenerationProcesses
{
    private static readonly TimeSpan Deadline = TimeSpan.FromSeconds(45);

    public static async Task<int> RunAsync(string[] args)
    {
        try
        {
            switch (args.ElementAtOrDefault(0) ?? "selftest")
            {
                case "selftest":
                    await SelfTestAsync(args.ElementAtOrDefault(1) ?? Path.GetTempPath());
                    break;
                case "owner":
                    using (var generation = ContentGeneration.Open(args[1], args[2]))
                    {
                        Console.WriteLine("owned");
                        while (await Console.In.ReadLineAsync() == "read")
                            Console.WriteLine(File.ReadAllText(Path.Combine(generation.RootPath,
                                generation.Manifest.Entries.Single().ArtifactLocator)));
                    }
                    break;
                case "build":
                    using (var result = Build(args[1], args[2]))
                    {
                        Require(result.Success, string.Join(";", result.Diagnostics.Select(d => d.Message)));
                        Console.WriteLine(result.GenerationId);
                        await Console.In.ReadLineAsync();
                    }
                    break;
                case "blocked-build":
                    using (var result = new ContentBuildCoordinator([new PausedProcessor()]).Build(Options(args[1], args[2])))
                        Require(result.Success, "blocked build failed");
                    break;
                case "collect":
                    ContentGeneration.CollectUnused(args[1]);
                    Console.WriteLine("collected");
                    break;
                case "acquire-once":
                    try
                    {
                        using var generation = ContentGeneration.Open(args[1], args[2]);
                        VerifyGeneration(generation, "{\"value\":1}");
                        Console.WriteLine("owned");
                    }
                    catch (DirectoryNotFoundException) { Console.WriteLine("missing"); }
                    break;
                default:
                    throw new ArgumentException("Unknown process-host mode.");
            }
            return 0;
        }
        catch (Exception error)
        {
            Console.Error.WriteLine(error);
            return 1;
        }
    }

    private static ContentBuildOptions Options(string source, string output) => new()
    {
        SourceRoot = source, OutputRoot = output, Namespace = "game", Target = AssetTarget.Client,
        RetainGeneration = true
    };

    private static ContentBuildResult Build(string source, string output) =>
        new ContentBuildCoordinator().Build(Options(source, output));

    private static async Task SelfTestAsync(string workspace)
    {
        string root = Path.Combine(Path.GetFullPath(workspace), "karpik-production-generation-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            Console.WriteLine($"Platform: {System.Runtime.InteropServices.RuntimeInformation.OSDescription}; runtime: {Environment.Version}; root: {root}");
            string sourceA = CreateSource(root, "source-a", 1);
            string sourceB = CreateSource(root, "source-b", 2);
            string output = Path.Combine(root, "output");
            using (var initial = Build(sourceA, output))
            {
                Require(initial.Success, "initial build failed");
                string oldId = initial.GenerationId!;
                string oldRoot = initial.PublishedGeneration!.RootPath;
                using var first = Start("owner", output, oldId);
                using var second = Start("owner", output, oldId);
                await first.ExpectAsync("owned");
                await second.ExpectAsync("owned");
                using var newer = Build(sourceB, output);
                Require(newer.Success, "replacement build failed");
                initial.Dispose();
                first.Kill();
                ContentGeneration.CollectUnused(output);
                Require(Directory.Exists(oldRoot), "one dead process released another owner's bytes");
                await second.SendAsync("read");
                await second.ExpectAsync("{\"value\":1}");
                second.Kill();
                ContentGeneration.CollectUnused(output);
                Require(!Directory.Exists(oldRoot), "last dead process retained an unused generation");
            }
            Console.WriteLine("PASS: production claims preserve lazy reads across publication and reclaim after final owner crash");

            using (var first = Start("build", sourceA, output))
            using (var second = Start("build", sourceB, output))
            {
                string firstId = await first.LineAsync();
                string secondId = await second.LineAsync();
                Require(firstId != secondId, "distinct contents published the same generation");
                using (var a = ContentGeneration.Open(output, firstId)) VerifyGeneration(a, "{\"value\":1}");
                using (var b = ContentGeneration.Open(output, secondId)) VerifyGeneration(b, "{\"value\":2}");
                using var current = ContentGeneration.OpenCurrent(output);
                Require(current.GenerationId == firstId || current.GenerationId == secondId, "current marker is not a complete build");
                first.Kill();
                second.Kill();
                ContentGeneration.CollectUnused(output);
            }
            Console.WriteLine("PASS: concurrent production publishers return complete retained generations and one coherent current marker");

            for (int iteration = 0; iteration < 12; iteration++)
            {
                using var initial = Build(sourceA, output);
                Require(initial.Success, "race setup publication failed");
                string id = initial.GenerationId!;
                using var holder = Start("owner", output, id);
                await holder.ExpectAsync("owned");
                using var replacement = Build(sourceB, output);
                Require(replacement.Success, "race replacement failed");
                initial.Dispose();
                holder.Kill(); // Leaves only crash-released claim metadata, without invoking collection.
                using var acquirer = Start("acquire-once", output, id);
                using var collector = Start("collect", output);
                string outcome = await acquirer.LineAsync();
                Require(outcome is "owned" or "missing", "unexpected production acquisition outcome");
                await collector.ExpectAsync("collected");
                await acquirer.CompleteAsync();
                await collector.CompleteAsync();
            }
            Console.WriteLine("PASS: 12 actual generation acquisition/collection process races preserve bytes or reject reclaimed generations");

            // Pause the actual build inside its processor, after it acquired the production namespace gate.
            File.WriteAllText(Path.Combine(sourceA, "a.json"), "{\"value\":3}");
            using (var blocked = Start("blocked-build", sourceA, output))
            {
                await blocked.ExpectAsync("processing");
                blocked.Kill();
            }
            using (var recovered = Build(sourceB, output))
            {
                Require(recovered.Success, "build gate remained held after process crash");
                VerifyGeneration(recovered.PublishedGeneration!, "{\"value\":2}");
            }
            Console.WriteLine("PASS: actual build gate is crash-released and next publication recovers");
            Console.WriteLine("ALL PRODUCTION GENERATION PROCESS CHECKS PASSED (current filesystem only)");
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    private static string CreateSource(string root, string name, int value)
    {
        string directory = Path.Combine(root, name);
        Directory.CreateDirectory(directory);
        string file = Path.Combine(directory, "a.json");
        File.WriteAllText(file, "{\"value\":" + value + "}");
        File.WriteAllText(file + ".meta", JsonSerializer.Serialize(new
        {
            schemaVersion = 1, assetId = "10000000-0000-0000-0000-000000000001", declaredType = "raw-json",
            logicalName = "game/a", importSettings = new { }, dependencies = Array.Empty<string>()
        }));
        return directory;
    }

    private static void VerifyGeneration(ContentGeneration generation, string expected)
    {
        Require(generation.Namespace == "game" && generation.Target == AssetTarget.Client, "generation identity was lost");
        var entry = generation.Manifest.Entries.Single();
        string artifact = Path.Combine(generation.RootPath, entry.ArtifactLocator);
        Require(File.ReadAllText(artifact) == expected, "immutable published bytes changed");
        Require(new FileInfo(artifact).Length == entry.Size, "manifest and artifact disagree");
    }

    private static Child Start(params string[] arguments)
    {
        var info = new ProcessStartInfo("dotnet")
        {
            RedirectStandardInput = true, RedirectStandardOutput = true, RedirectStandardError = true,
            UseShellExecute = false
        };
        info.ArgumentList.Add(Assembly.GetExecutingAssembly().Location);
        foreach (string argument in arguments) info.ArgumentList.Add(argument);
        return new Child(Process.Start(info) ?? throw new InvalidOperationException("Child did not start."));
    }

    private static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }

    private sealed class PausedProcessor : IContentProcessor
    {
        public string DeclaredType => "raw-json";
        public string Version => "production-crash-probe-v1";
        public ContentProcessorResult Process(ReadOnlySpan<byte> bytes, AssetMeta meta, string path, ContentProcessorContext context)
        {
            Console.WriteLine("processing");
            Console.In.ReadLine();
            return new RawJsonProcessor().Process(bytes, meta, path, context);
        }
    }

    private sealed class Child(Process process) : IDisposable
    {
        private readonly Task<string> _errors = process.StandardError.ReadToEndAsync();
        public async Task<string> LineAsync() => await process.StandardOutput.ReadLineAsync().WaitAsync(Deadline)
            ?? throw new InvalidOperationException("Child exited: " + await _errors);
        public async Task ExpectAsync(string expected)
        {
            string actual = await LineAsync();
            Require(actual == expected, $"Expected '{expected}', got '{actual}'.");
        }
        public async Task SendAsync(string value)
        {
            await process.StandardInput.WriteLineAsync(value);
            await process.StandardInput.FlushAsync();
        }
        public async Task CompleteAsync()
        {
            await process.WaitForExitAsync().WaitAsync(Deadline);
            Require(process.ExitCode == 0, "Child failed: " + await _errors);
        }
        public void Kill()
        {
            if (!process.HasExited) process.Kill(entireProcessTree: true);
            Require(process.WaitForExit(45000), "Child did not exit after kill.");
        }
        public void Dispose() { Kill(); process.Dispose(); }
    }
}
