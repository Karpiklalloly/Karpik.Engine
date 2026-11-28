using System.Diagnostics;
using Karpik.Engine.Shared.Modding;
using Karpik.Engine.Shared.Modding.Lua;
using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Threading.Channels;
using Karpik.Engine.Core;
using Xunit;

public sealed partial class ContentReloadHostTests
{
    public sealed class ModLiveTransitionEndToEndTests(Xunit.Abstractions.ITestOutputHelper output)
    {
        private const string Initial = """
            return {
              components = { Stats = { version = 1, fields = { Value = 'i32', Name = 'i32' }, wire = {'Value','Name'} } },
              systems = {{ id='state', phase='update', queries={q={inc={['state:Stats']={readonly=false}}}},
                callback=function(dt,ctx)
                  local q=ctx.queries.q; local p=q.components['state:Stats']
                  for e in q:entities() do return end
                  local s=p:new_snapshot(); s.Value=2147483647; s.Name=19
                  ctx.commands:add(p,ctx.commands:create(),s)
                end }}
            }
            """;
        private const string Migrated = """
            return {
              components = { Stats = { version = 2, fields = { Wide = 'i64', Renamed = 'i32', Added = 'i32' }, wire = {'Wide','Renamed','Added'},
                migrate = { from=1, fields={ Wide={from='Value',convert='i32_to_i64'}, Renamed={from='Name'}, Added={default=-77} } } } },
              on_reload = function() print('migrated-hook') end
            }
            """;

        [Theory]
        [InlineData(false)]
        [InlineData(true)]
        public Task PairedWorkersUseRealConnectionAndActualLuaContentCandidate(bool wrongPairSecret) =>
            RunPairedAsync(wrongPairSecret, systemCount: 1, measure: false);

        [Theory]
        [InlineData(1)]
        [InlineData(16)]
        public Task ModLiveTransition_ReportsPhaseMetrics(int systemCount) =>
            RunPairedAsync(wrongPairSecret: false, systemCount, measure: true);

        private async Task RunPairedAsync(bool wrongPairSecret, int systemCount, bool measure)
        {
            if (!OperatingSystem.IsLinux() && !OperatingSystem.IsWindows()) return;
            using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(40));
            var token = deadline.Token;
            using var serverTree = new HostTree(Side.Server);
            using var clientTree = new HostTree(Side.Client);
            Setup(serverTree, systemCount); Setup(clientTree, systemCount);
            var owner = new ModLiveLaunchOwner();
            int port = FreePort();
            string secret = RandomNumberGenerator.GetHexString(64);
            using var sa = owner.CreateAssociation(Side.Server);
            using var ca = owner.CreateAssociation(Side.Client);
            await using var server = new Child(serverTree, Side.Server, port, "paired", sa, secret, ca.Identity.Generation);
            await server.Until("live:listening", token);
            await using var client = new Child(clientTree, Side.Client, port, "paired", ca,
                wrongPairSecret ? RandomNumberGenerator.GetHexString(64) : secret, sa.Identity.Generation);
            await server.Until("live:connected", token);
            await client.Until("live:connected", token);
            await Task.WhenAll(server.Connected, client.Connected).WaitAsync(token);
            var pair = new ModLivePairedSession(server.Peer, client.Peer,
                (s, c) => s == sa.Identity && c == ca.Identity, TimeSpan.FromSeconds(8));
            await client.Send("input:7");
            await client.Until("live:effect:7", token); // application dispatched, not a transport ACK
            if (measure)
            {
                await SamplePeaks(WarmedTraffic(), "steady");
                await client.Send("input:7");
                await client.Until("live:effect:7", token);
            }
            var beforeServer = await server.Sample(token);
            var beforeClient = await client.Sample(token);
            Assert.Equal(1, beforeServer.Revision);
            Assert.Equal(1, beforeClient.Asset);
            Assert.Equal(int.MaxValue, beforeClient.Value);

            InstallPatch(serverTree); InstallPatch(clientTree);
            if (wrongPairSecret)
            {
                Assert.Null(await pair.DiscoverAsync(token));
                Assert.NotEqual(SimulationCodeReloadStatus.Success, await pair.ApplyAsync(["patch", "state"], token));
                Assert.Equal(beforeClient, await client.Sample(token));
                Assert.Equal(beforeServer, await server.Sample(token));
                return;
            }

            var found = await pair.DiscoverAsync(token);
            Assert.NotNull(found);
            Assert.Contains("patch", found.Value.Client);
            Assert.Equal(beforeClient, await client.Sample(token)); // discovery is not activation
            await server.Send("baseline-growth:1024");
            await server.Until("live:baseline-growth:1024", token);
            await Apply(SimulationCodeReloadStatus.Rejected, "patch", "state"); // one visible row + 1024 permitted additions
            Assert.Equal(beforeServer, await server.Sample(token));
            Assert.Equal(beforeClient, await client.Sample(token));
            await server.Send("baseline-growth:1023");
            await server.Until("live:baseline-growth:1023", token);
            await Apply(SimulationCodeReloadStatus.Rejected, "patch"); // The removed mod still owns live ECS rows.
            Assert.Equal(beforeServer, await server.Sample(token));
            Assert.Equal(beforeClient, await client.Sample(token));
            File.WriteAllText(Entry(serverTree), "error('selection reread accepted Lua')");
            File.WriteAllText(Entry(clientTree), "error('selection reread accepted Lua')");
            if (!measure)
            {
                await client.Send("seed-client-replicas");
                await client.Until("live:replica-count:1025", token);
                await Apply(SimulationCodeReloadStatus.Rejected, "patch", "state");
                Assert.Equal(beforeServer, await server.Sample(token));
                Assert.Equal(beforeClient, await client.Sample(token));
                Assert.DoesNotContain("patch-start", server.Trace);
                Assert.DoesNotContain("patch-start", client.Trace);
                await client.Send("drop-last-client-replica");
                await client.Until("live:replica-count:1024", token); // Abort kept all prior rows alive.
            }
            await Apply(SimulationCodeReloadStatus.Success, "patch", "state");
            await server.Until("live:lua:patch-start", token);
            await client.Until("live:lua:patch-start", token);
            var afterServer = await server.Sample(token);
            var afterClient = await client.Sample(token);
            AssertTransition(beforeServer, afterServer, 8);
            AssertTransition(beforeClient, afterClient, 8);
            Assert.Equal(1, afterClient.OldClaim); // command completed without waiting for retained old content claim
            await client.Send("input:23");
            await client.Until("live:effect:23", token);
            Assert.Equal(23, (await client.Sample(token)).Effect);

            await Apply(SimulationCodeReloadStatus.Success, "state");
            await server.Until("live:lua:patch-destroy", token);
            await client.Until("live:lua:patch-destroy", token);
            var disabledServer = await server.Sample(token);
            var disabledClient = await client.Sample(token);
            AssertTransition(afterServer, disabledServer, 1);
            AssertTransition(afterClient, disabledClient, 1);
            await Apply(SimulationCodeReloadStatus.Success, "patch", "state");
            await server.Until("live:lua:patch-start", token);
            await client.Until("live:lua:patch-start", token);
            AssertTransition(disabledServer, await server.Sample(token), 8);
            AssertTransition(disabledClient, await client.Sample(token), 8);
            output.WriteLine("server: " + server.Trace);
            output.WriteLine("client: " + client.Trace);

            async Task WarmedTraffic()
            {
                for (int n = 30; n < 46; n++)
                {
                    await client.Send($"input:{n}");
                    await client.Until($"live:effect:{n}", token);
                }
            }

            async Task SamplePeaks(Task pending, string label)
            {
                long serverBytes = 0, clientBytes = 0;
                int serverThreads = 0, clientThreads = 0, samples = 0;
                do
                {
                    var s = server.Resources();
                    var c = client.Resources();
                    serverBytes = Math.Max(serverBytes, s.Bytes);
                    clientBytes = Math.Max(clientBytes, c.Bytes);
                    serverThreads = Math.Max(serverThreads, s.Threads);
                    clientThreads = Math.Max(clientThreads, c.Threads);
                    samples++;
                    if (pending.IsCompleted) break;
                    token.ThrowIfCancellationRequested();
                    await Task.WhenAny(pending, Task.Delay(2, token));
                } while (true);
                await pending;
                output.WriteLine($"paired-peak:n={systemCount}:{label}:samples={samples}:serverRss={serverBytes}:serverThreads={serverThreads}:clientRss={clientBytes}:clientThreads={clientThreads}");
            }

            async Task Apply(SimulationCodeReloadStatus expected, params string[] ids)
            {
                long started = Stopwatch.GetTimestamp();
                var pending = pair.ApplyAsync(ids, token);
                if (measure) await SamplePeaks(pending, expected.ToString());
                var actual = await pending;
                output.WriteLine(FormattableString.Invariant($"paired-apply:{string.Join(',', ids)}:{actual}:ms:{Stopwatch.GetElapsedTime(started).TotalMilliseconds:F3}"));
                Assert.True(actual == expected,
                    $"Apply({string.Join(',', ids)}) was {actual}, expected {expected}; server: {server.Trace}; client: {client.Trace}");
            }
        }

        [Fact]
        public async Task PairedWireLossWhilePreparedAbortsWithoutClosingWorkerIpc()
        {
            if (!OperatingSystem.IsLinux() && !OperatingSystem.IsWindows()) return;
            using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(30));
            var token = deadline.Token;
            using var serverTree = new HostTree(Side.Server);
            using var clientTree = new HostTree(Side.Client);
            Setup(serverTree); Setup(clientTree);
            var owner = new ModLiveLaunchOwner();
            int port = FreePort();
            string secret = RandomNumberGenerator.GetHexString(64);
            using var sa = owner.CreateAssociation(Side.Server);
            using var ca = owner.CreateAssociation(Side.Client);
            await using var server = new Child(serverTree, Side.Server, port, "paired", sa, secret, ca.Identity.Generation);
            await server.Until("live:listening", token);
            await using var client = new Child(clientTree, Side.Client, port, "paired", ca, secret, sa.Identity.Generation);
            await server.Until("live:connected", token);
            await client.Until("live:connected", token);
            await Task.WhenAll(server.Connected, client.Connected).WaitAsync(token);
            var pair = new ModLivePairedSession(server.Peer, client.Peer,
                (s, c) => s == sa.Identity && c == ca.Identity, TimeSpan.FromSeconds(8));
            Assert.NotNull(await pair.DiscoverAsync(token));
            await client.Send("input:7");
            await client.Until("live:effect:7", token);
            var beforeServer = await server.Sample(token);
            var beforeClient = await client.Sample(token);
            InstallPatch(serverTree); InstallPatch(clientTree);
            await server.Send("hold-prepared");
            await server.Until("live:will-hold-prepared", token);
            var apply = pair.ApplyAsync(["patch", "state"], token);
            await server.Until("live:prepared-held", token);
            await client.Send("disconnect-wire");
            Assert.Equal(SimulationCodeReloadStatus.Rejected, await apply.WaitAsync(TimeSpan.FromSeconds(4), token));
            Assert.False(server.Peer.Loss.IsCompleted);
            Assert.False(client.Peer.Loss.IsCompleted);
            Assert.Equal(beforeServer, await server.Sample(token));
            Assert.Equal(beforeClient, await client.Sample(token));
            Assert.DoesNotContain("patch-start", server.Trace);
            Assert.DoesNotContain("patch-start", client.Trace);
        }

#if DEBUG
        [Fact]
        public async Task DedicatedDebugMigratesOnBothConnectionsAndAbortsIfOneArtifactIsDenied()
        {
            if (!OperatingSystem.IsLinux() && !OperatingSystem.IsWindows()) return;
            using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(40));
            var token = deadline.Token;
            using var serverTree = new HostTree(Side.Server);
            using var aTree = new HostTree(Side.Client);
            using var bTree = new HostTree(Side.Client);
            Setup(serverTree); Setup(aTree); Setup(bTree);
            int port = FreePort();
            await using var server = new Child(serverTree, Side.Server, port, "dedicated");
            await server.Until("live:listening", token);
            await using var a = new Child(aTree, Side.Client, port, "dedicated");
            await using var b = new Child(bTree, Side.Client, port, "dedicated");
            await server.Until("live:connected:2", token);
            await a.Until("live:connected", token);
            await b.Until("live:connected", token);
            await a.Send("input:11"); await a.Until("live:effect:11", token);
            await b.Send("input:12"); await b.Until("live:effect:12", token);
            var s0 = await server.Sample(token); var a0 = await a.Sample(token); var b0 = await b.Sample(token);
            foreach (var tree in new[] { serverTree, aTree, bTree }) File.WriteAllText(Entry(tree), Migrated);
            await server.Send("reload"); await server.Until("live:reload:Unsupported", token);
            // Each side must locally authorize the concrete artifact. B does not yet grant it.
            await server.Send("grant-admin"); await server.Send(Grant(serverTree, ExecutionSide.Server)); await a.Send(Grant(aTree, ExecutionSide.Client));
            await server.Until("live:grant:artifact", token); await a.Until("live:grant:artifact", token);
            await server.Send("reload"); await server.Until("live:reload:Rejected", token);
            Assert.Equal(s0, await server.Sample(token)); Assert.Equal(a0, await a.Sample(token)); Assert.Equal(b0, await b.Sample(token));

            await b.Send(Grant(bTree, ExecutionSide.Client)); await b.Until("live:grant:artifact", token);
            await server.Send("reload"); await server.Until("live:reload:Success", token);
            var s1 = await server.Sample(token); var a1 = await a.Sample(token); var b1 = await b.Sample(token);
            AssertTransition(s0, s1, 1); AssertTransition(a0, a1, 1); AssertTransition(b0, b1, 1);
            Assert.All(new[] { s1, a1, b1 }, state => { Assert.Equal(-77, state.Added); Assert.Equal(19, state.Name); });
            await b.Send("input:29"); await b.Until("live:effect:29", token);
        }
        private static string Grant(HostTree tree, ExecutionSide side) => "grant-artifact:" +
            LuaSimulationPreparation.SchemaArtifactDigest(side, [("state", LuaSourceCapture.Capture(tree.ModDirectory("state"), side))]);
#endif

        private static void AssertTransition(State before, State after, int asset)
        {
            Assert.Equal(before.Revision + 1, after.Revision);
            Assert.Equal(before.Entity, after.Entity);
            Assert.Equal(before.Peer, after.Peer);
            Assert.Equal(before.Connections, after.Connections);
            Assert.Equal(before.Value, after.Value);
            Assert.Equal(asset, after.Asset);
        }

        private static void InstallPatch(HostTree tree)
        {
            tree.BuildMod("patch", 8, contentOnly: false);
            File.WriteAllText(Path.Combine(tree.ModDirectory("patch"), "Shared", "main.lua"), """
                local started = false
                return {
                  on_start = function() assert(not started); started = true; print('patch-start') end,
                  on_destroy = function() assert(started); print('patch-destroy') end
                }
                """);
        }

        private static void Setup(HostTree tree, int systemCount = 1)
        {
            using var build = tree.Build(1);
            string entry = Entry(tree);
            Directory.CreateDirectory(Path.GetDirectoryName(entry)!);
            File.WriteAllText(Path.Combine(tree.ModDirectory("state"), "mod_info.json"), """{"id":"state"}""");
            File.WriteAllText(entry, systemCount == 1 ? Initial :
                Initial.Replace("return {", "local mod = {", StringComparison.Ordinal) + "\n" + $$"""
                for n = 2, {{systemCount}} do
                  mod.systems[n] = { id='observe' .. n, phase='update', queries={q={inc={['state:Stats']={readonly=true} } } },
                    callback=function(_, ctx)
                      local q=ctx.queries.q; local p=q.components['state:Stats']
                      for e in q:entities() do assert(p:get(e).Value == 2147483647) end
                    end }
                end
                return mod
                """);
        }
        private static string Entry(HostTree tree) => Path.Combine(tree.ModDirectory("state"), "Shared", "main.lua");
        private static int FreePort()
        {
            using var socket = new Socket(AddressFamily.InterNetwork, SocketType.Dgram, ProtocolType.Udp);
            socket.Bind(new IPEndPoint(IPAddress.Loopback, 0));
            return ((IPEndPoint)socket.LocalEndPoint!).Port;
        }

        private readonly record struct State(long Revision, long Entity, int Peer, int Connections, long Value, int Name, int Added, int Asset, int Effect, int OldClaim);
        private sealed class Child : IAsyncDisposable
        {
            private readonly Process _process;
            private readonly IpcServer _pipe;
            private readonly Channel<string> _lines = Channel.CreateUnbounded<string>();
            private readonly System.Collections.Concurrent.ConcurrentQueue<string> _trace = new();
            internal string Trace => string.Join(" | ", _trace);
            private readonly Task _read;
            private readonly Task<string> _errors;
            internal Task Connected { get; }
            internal ModLiveIpcPeer Peer => _pipe.ModLivePeer!;

            internal Child(HostTree tree, Side side, int port, string mode, ModLiveLaunchAssociation? association = null,
                string? pairSecret = null, ulong remoteGeneration = 0)
            {
                string pipe = "mod-live-e2e-" + Guid.NewGuid().ToString("N");
                _pipe = new IpcServer(pipe, Microsoft.Extensions.Logging.Abstractions.NullLoggerFactory.Instance);
                var info = new ProcessStartInfo("dotnet")
                {
                    WorkingDirectory = tree.Root, RedirectStandardInput = true, RedirectStandardOutput = true,
                    RedirectStandardError = true, UseShellExecute = false
                };
                info.ArgumentList.Add(typeof(Karpik.Content.Reload.HeadlessHost.HostMarker).Assembly.Location);
                info.ArgumentList.Add(side.ToString());
                info.ArgumentList.Add("--content-reload-instance"); info.ArgumentList.Add("live-host");
                info.ArgumentList.Add("--pipe-name"); info.ArgumentList.Add(pipe);
                info.Environment["KARPIK_HEADLESS_LIVE_MODE"] = mode;
                info.Environment["KARPIK_HEADLESS_LIVE_PORT"] = port.ToString(System.Globalization.CultureInfo.InvariantCulture);
                if (association is not null)
                {
                    _pipe.ConfigureModLive(association);
                    info.Environment[ModLiveLaunchAssociation.EnvironmentKey] = association.ToLaunchValue();
                    info.Environment["KARPIK_HEADLESS_PAIR_KEY"] = pairSecret;
                    info.Environment["KARPIK_HEADLESS_PAIR_LOCAL"] = association.Identity.Generation.ToString();
                    info.Environment["KARPIK_HEADLESS_PAIR_REMOTE"] = remoteGeneration.ToString();
                    info.Environment["KARPIK_HEADLESS_PAIR_SESSION"] = association.Identity.Session.ToString("N");
                }
                Connected = _pipe.WaitForConnectionAsync();
                _process = Process.Start(info)!;
                _errors = _process.StandardError.ReadToEndAsync();
                _read = Task.Run(async () =>
                {
                    while (await _process.StandardOutput.ReadLineAsync() is { } line)
                        if (line.StartsWith("live:", StringComparison.Ordinal))
                        {
                            if (_trace.Count < 64) _trace.Enqueue(line);
                            _lines.Writer.TryWrite(line);
                        }
                    _lines.Writer.Complete();
                });
            }

            internal (long Bytes, int Threads) Resources()
            {
                _process.Refresh();
                return (_process.WorkingSet64, _process.Threads.Count);
            }

            internal Task Send(string command) => _process.StandardInput.WriteLineAsync(command);
            internal async Task<string> Until(string prefix, CancellationToken token)
            {
                while (await _lines.Reader.WaitToReadAsync(token))
                    while (_lines.Reader.TryRead(out string? line))
                        if (line.StartsWith(prefix, StringComparison.Ordinal)) return line;
                await _process.WaitForExitAsync(token);
                throw new InvalidOperationException($"Worker did not emit {prefix}. Exit={_process.ExitCode}. {await _errors}");
            }
            internal async Task<State> Sample(CancellationToken token)
            {
                await Send("sample");
                var row = (await Until("live:sample:", token))[12..].Split(',');
                return new(long.Parse(row[0]), long.Parse(row[1]), int.Parse(row[2]), int.Parse(row[3]), long.Parse(row[4]),
                    int.Parse(row[5]), int.Parse(row[6]), int.Parse(row[7]), int.Parse(row[8]), int.Parse(row[9]));
            }
            public async ValueTask DisposeAsync()
            {
                _process.StandardInput.Close();
                if (!_process.HasExited)
                {
                    try { await _pipe.SendShutdownRequestAsync(TimeSpan.FromSeconds(2)); }
                    catch (Exception e) when (e is IOException or InvalidOperationException or OperationCanceledException) { }
                    try { await _process.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(3)); }
                    catch (TimeoutException) { if (!_process.HasExited) _process.Kill(entireProcessTree: true); }
                }
                await _process.WaitForExitAsync();
                await _read;
                _process.Dispose(); _pipe.Dispose();
            }
        }
    }
}
