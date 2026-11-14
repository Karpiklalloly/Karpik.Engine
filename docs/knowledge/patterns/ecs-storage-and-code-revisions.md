# ECS-хранилище и версии исполняемого кода

Если queries, VM и component storage имеют одного владельца, освобождение старых привязок при reload может уничтожить сохраняемые данные. Мир, native/dynamic pools и pointer policy должны жить до завершения Simulation; VM, callback capabilities, queries/listeners и command scratch — до retirement своей code revision.

Новая revision получает отдельный lifetime gate и подготовленные привязки к тому же storage. До commit доступ закрыт. При retirement старый gate инвалидируется, listeners и scratch освобождаются, компоненты сохраняются. Совпадение system ID не делает старый handle действительным в новой версии. Завершение Simulation закрывает все оставшиеся revisions и затем storage.

Разрешение имён, создание metadata и резервирование буферов выполняются вне тика. В hot path сохраняются подготовленные handles и прямой доступ к плотным pools. Проверять сохранение entity generations и значений, запрет старых views/snapshots, повторный reload и освобождение старых VM через WeakReference.

[Решение о владении](../../02_ADR/lua-code-revision-lifetime.md) · [Preparer/factory](../../../Modules/Shared/Modding/Modding.Core/Bindings/ComponentQueryPreparer.cs) · [Тесты revisions](../../../tests/Karpik.Engine.Core.Runner.Tests/LuaHotReloadBindingsTests.cs)
