# Общий maintenance barrier для Simulation и render

Ожидание simulation worker само по себе не исключает одновременное чтение старой версии главным потоком render. Перед заменой runtime закрывать вход в новые frame callbacks, завершать текущий gameplay frame/jobs и render access, затем удерживать worker reservation до конца подготовки, commit и cleanup. Одноразовый InvokeAsync acknowledgement не удерживает worker остановленным после возврата.

Main-thread control queue продолжает обслуживаться без обычных кадров. Snapshot/state requests, которые ждут зарезервированный worker, нужно отклонять: иначе control pump заблокируется на собственной maintenance-паузе. При shutdown сначала закрывать frame entry и дожидаться выполняющихся callbacks/подготовки, затем освобождать scopes.

Время maintenance исключать из variable dt и fixed backlog, сохраняя остаток до следующего fixed deadline. Проверять порядок через управляемые события и часы: отсутствие перекрытия доступа, удержание queued work, cancellation/shutdown и отсутствие catch-up после паузы. Измерять длительность паузы отдельно от обычного тика.

[Host boundary](../../../src/Karpik.Engine.Core.Runner/WorkerHost.cs) · [Worker reservation](../../../src/Karpik.Engine.Core/ClientSimulationWorker.cs) · [Сквозные тесты](../../../tests/Karpik.Engine.Core.Runner.Tests/LuaHotReloadOrchestrationTests.cs) · [Fixed time](../../../tests/Karpik.Engine.Core.Runner.Tests/LuaHotReloadTimingTests.cs)
