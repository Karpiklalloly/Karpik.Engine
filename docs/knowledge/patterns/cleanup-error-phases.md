# Классификация ошибок cleanup по фазе

Класс исключения не доказывает, что ошибся пользовательский callback. Например, InterpreterException может прийти и из host runtime.Dispose. Сохранять фазу сбоя: ошибка Lua destroy/unload-hook допускает логирование и продолжение очистки; отказ освобождения host resources означает недоказанную целостность и требует остановки Simulation.

При обработке нескольких владельцев идти в обратном порядке, попытаться выполнить каждый hook и Dispose, собрать ошибки. Ошибка logger тоже является host failure: сохранить её и продолжить cleanup остальных владельцев. Первый сбой не должен скрывать следующие или пропускать освобождение ресурсов.

После commit нельзя возвращаться к частично освобождённой старой версии. До commit возобновлять прежнюю версию можно только после успешной host-очистки кандидата. Проверять отдельно failing hook, host Dispose с тем же типом исключения, отказ logger и фактический unload остальных VM.

[Фазы ModContainer](../../../Modules/Shared/Modding/Modding.Core/ModContainer.cs) · [Best-effort cleanup](../../../Modules/Shared/Modding/Modding.Lua/LuaReloadCleanup.cs) · [Регрессии](../../../tests/Karpik.Engine.Core.Runner.Tests/LuaHotReloadLifecycleTests.cs)
