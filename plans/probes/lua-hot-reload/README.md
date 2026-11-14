# Lua code reload resource probe

Workload исполняет настоящие Lua-файлы и EngineRunner host API в отдельном filtered test run. Это не helper-only benchmark и не production latency SLA. Runner/CLR/sampler overhead явно входит в process metrics. Источник harness: `tests/Karpik.Engine.Core.Runner.Tests/LuaHotReloadIntegrationTests.cs`, `LuaHotReload_ReportsMaintenanceResourcesForRepeatedRealReloads`.

Из `/home/artem/git/Karpik.Engine`, после свежей Release build:

```bash
DOTNET_CLI_HOME=/tmp/karpik-dotnet-cli NUGET_PACKAGES=/home/artem/.nuget/packages VSTEST_CONNECTION_TIMEOUT=180 dotnet test tests/Karpik.Engine.Core.Runner.Tests/Karpik.Engine.Core.Runner.Tests.csproj -c Release --no-build --no-restore -m:1 -nr:false --filter 'FullyQualifiedName~LuaHotReload_ReportsMaintenanceResourcesForRepeatedRealReloads' --logger trx
```

VSTest требует разрешённый loopback socket; sandbox может запретить его до начала tests. Не считать aborted run результатом benchmark.

N=0/1/16 Lua-систем, 16 сущностей, 20 успешных замен после initial startup. Каждый system объявляет native position + dynamic Temperature query. Сохраняется один мир. Записываются per-reload pause/prepare/commit/retire seconds и process allocation delta, общие wall/pause/allocation, sampled managed/working-set/thread peaks. Sampler работает с интервалом2ms; peak является нижней sampled оценкой, sampler allocation записывается отдельно. Счётчики процессов не являются числом исключительно engine workers.

Полные per-case строки доступны в StdOut соответствующих UnitTestResult в TRX; извлечённый успешный report — `results.txt` в этом каталоге. Версию source/DLL и ограничения читать в [evidence](../../lua-hot-reload-evidence.md). Warmed common tick проверяется отдельно методом `LuaHotReload_ZeroSystemCommonTicksAllocateZeroBeforeAndAfterReload`; он не превращает аллоцирующую Lua VM в no-GC backend.
