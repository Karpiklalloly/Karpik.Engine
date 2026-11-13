# Common structural-command probe

Run only after a fresh Release Runner build. This console probe references the built test output DLLs and does not rebuild production projects. The original /tmp flat probe was extended here with dynamic and generated-native nested/fixed workloads, using the same final DLLs; the reference path is relative for repository portability.

From repository root:

```sh
DOTNET_CLI_HOME=/tmp/karpik-dotnet-cli NUGET_PACKAGES=/home/artem/.nuget/packages dotnet restore plans/probes/lua-structural-commands/Probe.csproj --ignore-failed-sources
DOTNET_CLI_HOME=/tmp/karpik-dotnet-cli dotnet run --project plans/probes/lua-structural-commands/Probe.csproj -c Release --no-restore -m:1 -nr:false
```

Measures common creation with flat dynamic (two leaves), nested/fixed dynamic (five leaves) and generated native nested/fixed (five writable leaves plus readonly leaves) payloads. C=2*N; V=2*N or5*N. Uses the existing NativeCompositeBindingsTests.NativeShape binding already compiled in Runner.Tests; no production rebuild required. Validates copied and readonly/default values outside measured regions. Setup, snapshot creation and world cleanup are outside measured steady-state regions. Includes cold call and 32 warmup + 64 measured calls at N=0/1/16/256/4096; asserts zero measured managed allocation. Reports p50/p95/max raw timings, not a latency SLA. Tiered JIT, OS scheduling and host load affect outliers. No Lua or third-party source changes.

The durable acceptance report is `../../lua-structural-commands-evidence.md`.
