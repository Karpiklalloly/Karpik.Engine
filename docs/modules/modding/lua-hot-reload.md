# Локальный hot reload Lua-кода

Контракт: [design r10](../../../plans/lua-hot-reload-design.md), [ExecPlan и состояние приёмки](../../../plans/lua-hot-reload-execplan.md), [evidence](../../../plans/lua-hot-reload-evidence.md). Lua остаётся экспериментальным backend.

## Host API

До Setup host явно объявляет Simulation локальной:

```csharp
runner.EnableLocalCodeReload();
// Register modules/composition, then Setup/SetupAsync as usual.
```

После startup host ставит запрос и продолжает прокачивать main-thread scheduler:

```csharp
Task<SimulationCodeReloadResult> request = runner.RequestCodeReloadAsync(cancellationToken);
scheduler.RunUntilCompleted(request); // Only the owning main thread may pump.
SimulationCodeReloadResult result = request.GetAwaiter().GetResult();
```

Без opt-in возвращается `Unsupported`. Сетевые hosts не должны разрешать local-only режим: этот API не определяет подключение по Side.Client/Server и не согласует участников сети. Запрос без backend тоже Unsupported; до startup, во время shutdown или после execution fault — InvalidState. Только одна операция работает до конца cleanup, конкурентная возвращает Busy, очередь запросов отсутствует.

Запрос можно поставить из другого потока. Нельзя синхронно ждать его внутри gameplay/render callback: текущий callback должен завершиться для входа в barrier. Host обязан прокачивать scheduler, исключая обычные кадры во время `IsCodeReloading`. Runner также закрывает вход в свои frame callbacks и ждёт уже исполняющиеся вызовы.

Встроенный WorkerHost держит отдельную maintenance reservation настоящего ClientSimulationWorker и исключает render/frame-begin, pending snapshot work и новые frame submissions. Snapshot/restart-state requests во время операции возвращают null/отклоняются. Custom hosts с дополнительной фоновой работой обязаны установить `ISimulationCodeReloadBoundary` через `SetCodeReloadBoundary` до запросов. Lease исключает всю работу с заменяемыми ресурсами до завершения cleanup, затем освобождается на main thread.

Результаты:

| Status | Поведение |
| --- | --- |
| Success | Новая версия активна; старые runtime и scheduler очищены |
| Rejected | Подготовка не прошла, после успешной cleanup прежняя версия продолжает работу; Error содержит причину |
| Cancelled | Отмена до Commit, активная версия не менялась |
| Busy / Unsupported / InvalidState | Операция не начата |
| Faulted | Целостность host resources не доказана либо случился инфраструктурный сбой Commit/retirement; Simulation остановлена |

После Faulted host прекращает тики, ориентируясь на `IsApplicationRunning`, и выполняет shutdown. Нельзя возобновлять старую частично освобождённую версию. Cancellation действует до последней проверки перед Commit; после неё переключение и cleanup завершаются независимо от токена. `DrainCodeReloadForShutdownAsync` необратимо закрывает frame entry, отменяет подготовку и ждёт безопасной очистки до disposal Simulation scopes. Обычную отмену reload выполняют токеном запроса; зависший синхронный Lua-код принудительно не прерывается.

## Что заменяется

Один запрос читает Lua-файлы всех уже выбранных модов и готовит одну revision. Можно добавлять/удалять Lua Update-системы, менять queries, accesses и order. Набор модов, metadata, schemas, pointer policy и CLR-код фиксированы. Повторного catalog Scan нет. Изменение ID/version/field kind/nested/fixed/pointer schema отклоняет весь кандидат; только перестановка полей с той же канонической схемой допустима.

Мир, entity IDs/generations, native/dynamic компоненты и экземпляры обычных CLR-систем сохраняются. Pipeline Init/Destroy не повторяются. Enabled-флаги сохраняются по mod ID. Legacy on_update/on_fixed_update и отдельные объявленные системы используют одну активную revision. Индивидуальные FixedUpdate-адаптеры этим срезом не добавляются.

## Lua lifecycle

```lua
return {
    on_load = function()
        -- Initialize this VM's local state; no ECS access.
    end,
    on_start = function()
        -- Initial launch only, not repeated by reload.
    end,
    on_reload = function()
        -- Optional: initialize local state for a replacement VM.
        -- No ECS capability and no automatic globals/closures transfer.
    end,
    on_destroy = function()
        -- Release runtime-owned state. During retirement, old ECS access is revoked.
    end,
    on_unload = function()
        -- Final cleanup; runs even if a replacement's on_load failed partway.
    end
}
```

Порядок: top-level → on_load всех модов → проверка schemas/queries/общего графа → on_reload всех модов → Commit. Сохраняются [порядок зависимостей модов и результаты `has_dependency`](mod-dependencies.md) с первого запуска; Shared загружается раньше side. Успешная новая версия не вызывает on_start, но считается активированной и позднее получает on_destroy. Ошибка load/reload или графа отклоняет всю замену. Непринятый кандидат получает unload без destroy; старая активная версия после Commit получает destroy и unload.

Старые handles, query, views и snapshots с прежними access capabilities становятся недействительными, включая повторно используемый system ID. VM globals, closures и coroutines не переносятся. Сохраняемое gameplay state должно находиться в ECS. Во время подготовки/retirement callbacks не получают ECS-доступа; чтение файлов и диагностические логи не откатываются, произвольные сторонние host effects транзакционными не объявляются.

Cleanup выполняется в обратном порядке и продолжает остальные callbacks/ресурсы после ошибки. Ошибка именно Lua-hook логируется; сбой runtime.Dispose или иных host resources фатален независимо от класса исключения. Запрет rollback действует после входа в Commit.

## Пауза и измерения

Подготовка, создание workers/буферов и cleanup выполняются вне обычного тика. В обычном warmed common-пути новые аллокации не добавлены; прежние Lua allocation ceilings сохраняются. Пауза начинается после quiescence текущего кадра и workers. User pause state сохраняется, время maintenance не увеличивает variable dt, TotalTime или fixed backlog. Fixed ticker сохраняет остаток до следующего deadline.

Custom host, считающий dt по wall clock, использует виртуальное время `stopwatch.Elapsed.TotalSeconds - runner.CodeReloadPausedSeconds` и обновляет sample после control pump. Встроенный WorkerHost делает это для client/server loops.

`LastCodeReloadMetrics` — согласованный snapshot последней операции: PauseSeconds, PreparationSeconds, CommitSeconds, RetirementSeconds и ProcessAllocatedBytes. Allocation — process-wide delta, включая конкурентную работу и instrumentation; это не бюджет одного потока. Quiescing/очередь учитываются отдельно при внешнем wall-clock measurement. Пиковая память/threads — отдельные probes; [команды](../../../plans/probes/lua-hot-reload/README.md).

Не заявляются network/save integration, миграции, NativeAOT, editor UI/watcher, hot reload CLR-assembly или ограничение времени исполнения Lua.
