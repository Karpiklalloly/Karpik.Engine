using System.Diagnostics;
using System.Reflection;

// Feasibility experiment only: cold control-plane operations, never a frame-path API.
return await Probe.RunAsync(args);

internal static class Probe
{
    private static readonly TimeSpan Deadline = TimeSpan.FromSeconds(15);

    public static async Task<int> RunAsync(string[] args)
    {
        try
        {
            if (args.Length == 0 || args[0] == "selftest")
            {
                await SelfTestAsync(args.Length > 1 ? args[1] : Path.GetTempPath());
                return 0;
            }

            string root = args[1];
            bool trace = args.Length > 2 && args[2] == "trace";
            switch (args[0])
            {
                case "owner":
                    using (Acquire(root))
                    {
                        Console.WriteLine("owned");
                        await Console.In.ReadLineAsync();
                    }
                    break;
                case "gate":
                    using (Gate(root, trace))
                    {
                        Console.WriteLine("locked");
                        await Console.In.ReadLineAsync();
                    }
                    break;
                case "collect-paused":
                    using (Gate(root))
                    {
                        Console.WriteLine("checked");
                        await Console.In.ReadLineAsync();
                        Directory.Delete(Generation(root), recursive: true);
                        Console.WriteLine("collected");
                    }
                    break;
                case "collect":
                    Console.WriteLine(Collect(root, trace) ? "collected" : "retained");
                    break;
                case "attempt":
                    try
                    {
                        using var claim = Acquire(root, trace);
                        Console.WriteLine("owned");
                        Require(File.ReadAllText(Artifact(root)) == "immutable", "acquired artifact disappeared");
                    }
                    catch (DirectoryNotFoundException)
                    {
                        Console.WriteLine("missing");
                    }
                    break;
                default:
                    throw new ArgumentException("Unknown probe mode.");
            }
            return 0;
        }
        catch (Exception error)
        {
            Console.Error.WriteLine(error);
            return 1;
        }
    }

    private static string Generation(string root) => Path.Combine(root, "generation");
    private static string Artifact(string root) => Path.Combine(Generation(root), "artifact.bin");

    private static FileStream Gate(string root, bool trace = false, Action? onContention = null)
    {
        // This file is deliberately NEVER removed/replaced: waiters must share one inode/file identity.
        Directory.CreateDirectory(root);
        var timeout = Stopwatch.StartNew();
        bool reportedContention = false;
        while (true)
        {
            try
            {
                return new FileStream(Path.Combine(root, "namespace.gate"), FileMode.OpenOrCreate,
                    FileAccess.ReadWrite, FileShare.None);
            }
            catch (IOException) when (timeout.Elapsed < Deadline)
            {
                if (!reportedContention)
                {
                    reportedContention = true;
                    if (trace)
                        Console.WriteLine("blocked");
                    onContention?.Invoke();
                }
                Thread.Sleep(2);
            }
        }
    }

    private static Claim Acquire(string root, bool trace = false)
    {
        using var gate = Gate(root, trace);
        if (!Directory.Exists(Generation(root)))
            throw new DirectoryNotFoundException("Generation was already collected.");
        string path = Path.Combine(Generation(root), Guid.NewGuid().ToString("N") + ".claim");
        return new Claim(new FileStream(path, FileMode.CreateNew, FileAccess.ReadWrite, FileShare.Read));
    }

    private static bool Collect(string root, bool trace = false)
    {
        using var gate = Gate(root, trace);
        if (!Directory.Exists(Generation(root)))
            return true;
        try
        {
            // Reject unknown entries rather than deleting data whose ownership is unproven.
            foreach (string entry in Directory.EnumerateFileSystemEntries(Generation(root)))
            {
                if (Path.GetFileName(entry) == "artifact.bin" && File.Exists(entry))
                    continue;
                if (!entry.EndsWith(".claim", StringComparison.Ordinal) || Directory.Exists(entry))
                    return false;
                using var stale = new FileStream(entry, FileMode.Open, FileAccess.ReadWrite, FileShare.None);
            }
            // All acquisition happens under gate, so no new claim can appear between checking and deleting.
            Directory.Delete(Generation(root), recursive: true);
            return true;
        }
        catch (IOException)
        {
            return false;
        }
        catch (UnauthorizedAccessException)
        {
            return false;
        }
    }

    private static void Publish(string root)
    {
        using var gate = Gate(root);
        Directory.CreateDirectory(Generation(root));
        File.WriteAllText(Artifact(root), "immutable");
    }

    private static async Task SelfTestAsync(string workspace)
    {
        if (!OperatingSystem.IsLinux() && !OperatingSystem.IsWindows())
            throw new PlatformNotSupportedException("This probe targets Windows and Linux only.");
        string baseRoot = Path.Combine(Path.GetFullPath(workspace), "karpik-generation-probe-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(baseRoot);
        try
        {
            Console.WriteLine($"Platform: {System.Runtime.InteropServices.RuntimeInformation.OSDescription}; runtime: {Environment.Version}; root: {baseRoot}");
            string root = Path.Combine(baseRoot, "owners");
            Publish(root);
            using (var first = Start("owner", root))
            using (var second = Start("owner", root))
            {
                await first.ExpectAsync("owned");
                await second.ExpectAsync("owned");
                await ExpectCollectorAsync(root, "retained");
                first.Kill();
                await ExpectCollectorAsync(root, "retained");
                Require(File.ReadAllText(Artifact(root)) == "immutable", "second owner lost bytes after first died");
                second.Kill();
                await ExpectCollectorAsync(root, "collected");
            }
            Console.WriteLine("PASS: two processes own concurrently; crash releases only dead owner's claim; final crash permits reclamation");

            Publish(root);
            using (var first = Acquire(root))
            {
                using (var second = Acquire(root))
                {
                    await ExpectCollectorAsync(root, "retained");
                    first.Dispose();
                    await ExpectCollectorAsync(root, "retained");
                }
                await ExpectCollectorAsync(root, "collected");
            }
            Console.WriteLine("PASS: independently opened claims in the same process do not invalidate each other's ownership");

            Task<FileStream> waitingGate;
            var contention = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            using (Gate(root))
            {
                waitingGate = Task.Run(() => Gate(root, onContention: () => contention.SetResult()));
                await contention.Task.WaitAsync(Deadline);
                Require(!waitingGate.IsCompleted, "independent same-process gate opens bypassed exclusion");
            }
            using (await waitingGate.WaitAsync(Deadline)) { }
            Console.WriteLine("PASS: independently opened namespace gates exclude each other in the same process");

            Publish(root);
            using (var holder = Start("gate", root))
            {
                await holder.ExpectAsync("locked");
                using var collector = Start("collect", root, trace: true);
                await collector.ExpectAsync("blocked");
                Require(!collector.Process.HasExited, "collector bypassed namespace gate");
                holder.Kill();
                await collector.ExpectAsync("collected");
                await collector.CompleteAsync();
            }
            Console.WriteLine("PASS: namespace gate serializes collection and is released on process death");

            Publish(root);
            using (var collector = Start("collect-paused", root))
            {
                await collector.ExpectAsync("checked");
                using var acquirer = Start("attempt", root, trace: true);
                await acquirer.ExpectAsync("blocked");
                Require(!acquirer.Process.HasExited, "acquirer bypassed collector gate after ownership check");
                await collector.Process.StandardInput.WriteLineAsync("continue");
                await collector.Process.StandardInput.FlushAsync();
                await collector.ExpectAsync("collected");
                await collector.CompleteAsync();
                await acquirer.ExpectAsync("missing");
                await acquirer.CompleteAsync();
            }
            Console.WriteLine("PASS: acquirer cannot enter collector's check/delete window and rejects deleted generation");

            Publish(root);
            Directory.CreateDirectory(Path.Combine(Generation(root), "uncertain.claim"));
            await ExpectCollectorAsync(root, "retained");
            Require(File.Exists(Artifact(root)), "uncertain ownership did not fail closed");
            Directory.Delete(Path.Combine(Generation(root), "uncertain.claim"));
            await ExpectCollectorAsync(root, "collected");
            Console.WriteLine("PASS: uncertain claim fails closed; retry reclaims after uncertainty is removed");

            for (int iteration = 0; iteration < 40; iteration++)
            {
                // Fresh generation namespaces exercise acquisition/delete races; first gate creation is tested separately.
                string raceRoot = Path.Combine(baseRoot, "race-" + iteration);
                Publish(raceRoot);
                using var acquirer = Start("attempt", raceRoot);
                using var collector = Start("collect", raceRoot);
                string outcome = await acquirer.LineAsync();
                Require(outcome is "owned" or "missing", "unexpected acquire outcome: " + outcome);
                await acquirer.CompleteAsync();
                string collection = await collector.LineAsync();
                Require(collection is "retained" or "collected", "unexpected collection outcome: " + collection);
                await collector.CompleteAsync();
                await ExpectCollectorAsync(raceRoot, "collected");
            }
            Console.WriteLine("PASS: 40 cross-process acquisition/collection races preserve acquired bytes or reject missing generation");

            string startupRoot = Path.Combine(baseRoot, "startup");
            using (var first = Start("gate", startupRoot, trace: true))
            using (var second = Start("gate", startupRoot, trace: true))
            {
                string[] states = await Task.WhenAll(first.LineAsync(), second.LineAsync());
                Require(states.Count(state => state == "locked") == 1 && states.Count(state => state == "blocked") == 1,
                    "simultaneous startup did not prove exactly one locked and one contending process");
                if (states[0] == "locked")
                {
                    first.Kill();
                    await second.ExpectAsync("locked");
                }
                else
                {
                    second.Kill();
                    await first.ExpectAsync("locked");
                }
            }
            Console.WriteLine("PASS: simultaneous first-open gate creation has one owner and recovers after winner death");
            Console.WriteLine("ALL PROBES PASSED (current platform/filesystem only)");
        }
        finally
        {
            Directory.Delete(baseRoot, recursive: true);
        }
    }

    private static async Task ExpectCollectorAsync(string root, string expected)
    {
        using var child = Start("collect", root);
        await child.ExpectAsync(expected);
        await child.CompleteAsync();
    }

    private static Child Start(string command, string root, bool trace = false)
    {
        var start = new ProcessStartInfo("dotnet")
        {
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false
        };
        start.ArgumentList.Add(Assembly.GetExecutingAssembly().Location);
        start.ArgumentList.Add(command);
        start.ArgumentList.Add(root);
        if (trace)
            start.ArgumentList.Add("trace");
        return new Child(Process.Start(start) ?? throw new InvalidOperationException("Could not start child."));
    }

    private static void Require(bool condition, string message)
    {
        if (!condition)
            throw new InvalidOperationException(message);
    }

    private sealed class Claim(FileStream stream) : IDisposable
    {
        private bool _disposed;
        public void Dispose()
        {
            if (_disposed)
                return;
            _disposed = true;
            stream.Dispose();
            // Retain stale claim files until collection; disposing a claim never mutates namespace membership.
        }
    }

    private sealed class Child(Process process) : IDisposable
    {
        public Process Process { get; } = process;
        private readonly Task<string> _errors = process.StandardError.ReadToEndAsync();
        public async Task<string> LineAsync() => await Process.StandardOutput.ReadLineAsync().WaitAsync(Deadline)
            ?? throw new InvalidOperationException("Child exited without response: " + await _errors);
        public async Task ExpectAsync(string expected) => Require(await LineAsync() == expected, "child response differs from " + expected);
        public async Task CompleteAsync()
        {
            await Process.WaitForExitAsync().WaitAsync(Deadline);
            Require(Process.ExitCode == 0, "child failed: " + await _errors);
        }
        public void Kill()
        {
            if (!Process.HasExited)
                Process.Kill(entireProcessTree: true);
            Require(Process.WaitForExit(15000), "killed child did not exit");
        }
        public void Dispose()
        {
            Kill();
            Process.Dispose();
        }
    }
}
