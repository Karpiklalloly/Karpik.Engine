# Инвалидирование очереди после перехваченной ошибки

`pcall` перехватывает Lua-ошибку, но не восстанавливает согласованность набора отложенных команд. Если одна операция отклонена, уже накопленная часть может содержать незавершённую инициализацию сущностей. Поэтому любая ошибка нового command API должна оставлять всю очередь текущего callback недействительной, даже если скрипт продолжил работу.

Устанавливать необратимый для этого callback флаг invalid и в общем C# слое, и при ошибках Lua-адаптера до вызова общего слоя: неверные аргументы, чужой handle, несовместимый snapshot, неправильный token. Переполнение подчиняется тому же правилу. Проверять invalid до пустой очереди и любых оптимизированных путей применения.

Ошибка callback или preflight отбрасывает все отложенные изменения; ранее выполненные немедленные записи сохраняются. Предсказуемые ошибки следуют существующей StopSimulation/LogAndContinue policy. Неожиданный playback failure останавливает Simulation без обещания rollback. Любое исключение во время playback, включая отмену, также инвалидирует очередь: перехват исключения C# вызывающим кодом не должен разрешать повторное применение уже выполненной части.

В `finally` завершать invocation, очищать очередь и закрывать доступ. Только следующий callback получает новое действительное состояние; старые pending tokens остаются просроченными. Проверять не только исключение, но и отсутствие всех отложенных изменений, сохранение immediate writes и восстановление работы следующего callback.

[Common buffer](../../../Modules/Shared/Modding/Modding.Core/Bindings/StructuralCommandBuffer.cs) · [Lua adapter](../../../Modules/Shared/Modding/Modding.Lua/LuaEcsContext.cs) · [pcall тесты](../../../tests/Karpik.Engine.Core.Runner.Tests/LuaStructuralCommandTests.cs) · [Отмена playback](../../../tests/Karpik.Engine.Core.Runner.Tests/StructuralCommandBufferTests.cs) · [API и error policies](../../modules/modding/structural-commands.md)
