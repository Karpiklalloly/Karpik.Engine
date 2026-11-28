# Lua code reload resource probe

Workload исполняет настоящие Lua-файлы и EngineRunner host API в отдельном filtered test run. Это не helper-only benchmark и не production latency SLA. Runner/CLR/sampler overhead явно входит в process metrics. Источник harness: `tests/Karpik.Engine.Core.Runner.Tests/LuaHotReloadIntegrationTests.cs`, `LuaHotReload_ReportsMaintenanceResourcesForRepeatedRealReloads`.

Из `/home/artem/git/Karpik.Engine`, после свежей Release build:

```bash
DOTNET_CLI_HOME=/tmp/karpik-dotnet-cli NUGET_PACKAGES=/home/artem/.nuget/packages VSTEST_CONNECTION_TIMEOUT=180 dotnet test tests/Karpik.Engine.Core.Runner.Tests/Karpik.Engine.Core.Runner.Tests.csproj -c Release --no-build --no-restore -m:1 -nr:false --filter 'FullyQualifiedName~LuaHotReload_ReportsMaintenanceResourcesForRepeatedRealReloads' --logger trx
```

VSTest требует разрешённый loopback socket; sandbox может запретить его до начала tests. Не считать aborted run результатом benchmark.

N=0/1/16 Lua-систем, 16 сущностей, 20 успешных замен после initial startup. Каждый system объявляет native position + dynamic Temperature query. Сохраняется один мир. Записываются per-reload pause/prepare/commit/retire seconds и process allocation delta, общие wall/pause/allocation, sampled managed/working-set/thread peaks. Sampler работает с интервалом2ms; peak является нижней sampled оценкой, sampler allocation записывается отдельно. Счётчики процессов не являются числом исключительно engine workers.

Полные per-case строки доступны в StdOut соответствующих UnitTestResult в TRX; извлечённый успешный report — `results.txt` в этом каталоге. Версию source/DLL и ограничения читать в [evidence](../../lua-hot-reload-evidence.md). Warmed common tick проверяется отдельно методом `LuaHotReload_ZeroSystemCommonTicksAllocateZeroBeforeAndAfterReload`; он не превращает аллоцирующую Lua VM в no-GC backend.

## Координированный переход двух процессов

Из корня, на свежих Release или Debug артефактах:

```sh
CI=true dotnet test tests/Karpik.Engine.Core.Runner.Tests/Karpik.Engine.Core.Runner.Tests.csproj -c Release -m:1 -nr:false --no-build --no-restore --filter 'FullyQualifiedName~PairedWorkersUseRealConnection&DisplayName~False' --logger 'console;verbosity=detailed' --logger trx
```

[Сценарий](../../../tests/Karpik.Engine.Core.Runner.Tests/ModLiveTransitionEndToEndTests.cs) на одной паре процессов выполняет Abort при живых строках, включение, выключение и повторное включение с удерживаемым Content claim. Строки `live:phase:<op>:…` и `live:metrics:<op>:…` привязывают время на стороне к ID операции; `paired-apply:…` измеряет сквозной запрос. `pauseMs` не включает игровой drain; `processAllocatedBytes` включает прочую работу процесса и не является аллокациями Commit. Release наблюдение: 150.625 мс Abort, 116.861 / 60.245 / 67.257 мс успешные переходы. Debug: 172.331 мс Abort, 134.501 / 60.393 / 88.451 мс успешные. Это по одному headless прогону без внешнего сэмплера, не SLA.

Чтобы сравнить прогретый обмен и переход на том же бинарнике, замените фильтр на `FullyQualifiedName~ModLiveTransition_ReportsPhaseMetrics`. Сценарий использует 1 или 16 действующих Lua-систем, снимает RSS и число потоков обоих процессов примерно раз в 2 мс из тестового процесса. Таблица — sampled Release, байты и максимум потоков; пики сервера и клиента не обязательно одновременные.

| Системы | Steady RSS, сервер / клиент | Максимум при Abort/Apply, сервер / клиент | Потоки, сервер / клиент |
|---|---:|---:|---:|
| 1 | 95 993 856 / 102 690 816 | 100 937 728 / 103 821 312 | 32 / 33 |
| 16 | 100 372 480 / 102 178 816 | 103 333 888 / 106 213 376 | 32 / 33 |

С сэмплером сквозные времена Release: 1 система — Abort 184.150 мс, Apply 143.655 / 61.739 / 66.428 мс; 16 систем — 184.373 мс и 144.431 / 93.057 / 67.794 мс. Удерживаемый старый Content claim не задержал Commit. RSS включает CLR/JIT и всё адресованное процессу, число потоков — не число engine workers, интервалы выборки не гарантируют точный пик. На Mesa llvmpipe проверен offscreen readback; физические GPU/VRAM не измерены.
